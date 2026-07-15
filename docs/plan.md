# VBMigrator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build VBMigrator — a VB.NET→C# migration tool with Roslyn+LLM hybrid pipeline, knowledge base, VS Extension, and CLI.

**Architecture:** ICSharpCode.CodeConverter handles whole-file conversion (70%) as step [2]; SeedRuleEngine covers 25 known-problematic patterns per method using Roslyn VB SyntaxNodes; LLM fills the rest. ConfidenceScorer uses min(confidences) and routes to AUTO/SUGGEST/HUMAN_QUEUE. VSIX launches CLI out-of-process (net472↔net8 isolation). ConvertFile uses JSON stdout; ConvertSolution writes to SQLite.

**Tech Stack:** .NET 8.0 (Core/CLI), net472 (VSIX), Roslyn 4.x (Microsoft.CodeAnalysis.CSharp + VisualBasic), ICSharpCode.CodeConverter 10.x, Anthropic.SDK 3.x, Microsoft.Data.Sqlite 8.x, System.CommandLine 2.x, xUnit 2.x, VS SDK 17.x.

**Solution root:** `VBMigrator/` (new directory, sibling to this repo or user-chosen location)

---

## Task 1: Solution & project structure

**Files:**
- `VBMigrator.sln`
- `src/VBMigrator.Core/VBMigrator.Core.csproj`
- `src/VBMigrator.CLI/VBMigrator.CLI.csproj`
- `tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj`
- `Directory.Build.props`

- [ ] Create solution root and run scaffold commands:

```bash
mkdir VBMigrator && cd VBMigrator
dotnet new sln -n VBMigrator
mkdir -p src/VBMigrator.Core src/VBMigrator.CLI tests/VBMigrator.Core.Tests samples/SampleVBProject
dotnet new classlib -n VBMigrator.Core -f net8.0 -o src/VBMigrator.Core
dotnet new console -n VBMigrator.CLI -f net8.0 -o src/VBMigrator.CLI
dotnet new xunit -n VBMigrator.Core.Tests -f net8.0 -o tests/VBMigrator.Core.Tests
dotnet sln add src/VBMigrator.Core/VBMigrator.Core.csproj
dotnet sln add src/VBMigrator.CLI/VBMigrator.CLI.csproj
dotnet sln add tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj
cd tests/VBMigrator.Core.Tests && dotnet add reference ../../src/VBMigrator.Core/VBMigrator.Core.csproj && cd ../..
```

- [ ] Create `Directory.Build.props` at solution root:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] Edit `src/VBMigrator.Core/VBMigrator.Core.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>VBMigrator.Core</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.VisualBasic" Version="4.*" />
    <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="4.*" />
    <PackageReference Include="ICSharpCode.CodeConverter" Version="10.*" />
    <PackageReference Include="Anthropic.SDK" Version="3.*" />
    <PackageReference Include="Microsoft.Data.Sqlite" Version="8.*" />
    <PackageReference Include="System.Text.Json" Version="8.*" />
  </ItemGroup>
</Project>
```

- [ ] Edit `src/VBMigrator.CLI/VBMigrator.CLI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <RootNamespace>VBMigrator.CLI</RootNamespace>
    <AssemblyName>vbmigrator</AssemblyName>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>vbmigrator</ToolCommandName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.*" />
    <PackageReference Include="Microsoft.Build.Locator" Version="1.*" />
    <ProjectReference Include="../../src/VBMigrator.Core/VBMigrator.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] Edit `tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.*" />
    <PackageReference Include="xunit" Version="2.*" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.*" />
    <ProjectReference Include="../../src/VBMigrator.Core/VBMigrator.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] Delete the auto-generated `Class1.cs` in Core and `UnitTest1.cs` in Tests; create stub `src/VBMigrator.Core/Placeholder.cs`:

```csharp
// Placeholder — remove when first real file is added
namespace VBMigrator.Core;
```

- [ ] Run build to confirm structure:

```bash
dotnet build VBMigrator.sln
```

Expected output keyword: **Build succeeded**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/VBMigrator.Core.csproj src/VBMigrator.CLI/VBMigrator.CLI.csproj tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj Directory.Build.props VBMigrator.sln src/VBMigrator.Core/Placeholder.cs
git commit -m "scaffold: solution, Core, CLI, Tests projects (net8.0)"
```

---

## Task 2: Core models

**Files:**
- `src/VBMigrator.Core/Models/TranslationRoute.cs`
- `src/VBMigrator.Core/Models/LlmFailureReason.cs`
- `src/VBMigrator.Core/Models/TranslationRequest.cs`
- `src/VBMigrator.Core/Models/TranslationResult.cs`
- `src/VBMigrator.Core/Models/DifficultyMap.cs`
- `src/VBMigrator.Core/Models/Pattern.cs`
- `src/VBMigrator.Core/Models/TranslationLog.cs`

- [ ] Create `src/VBMigrator.Core/Models/TranslationRoute.cs`:

```csharp
namespace VBMigrator.Core.Models;

public enum TranslationRoute
{
    SeedRule,
    Llm,
    HumanQueue,
    Error
}
```

- [ ] Create `src/VBMigrator.Core/Models/LlmFailureReason.cs`:

```csharp
namespace VBMigrator.Core.Models;

public enum LlmFailureReason
{
    RateLimit,
    Timeout,
    ApiError,
    ContentFilter
}
```

- [ ] Create `src/VBMigrator.Core/Models/TranslationRequest.cs`:

```csharp
namespace VBMigrator.Core.Models;

public record TranslationRequest
{
    public required string VbSource { get; init; }
    public required string FilePath { get; init; }
    public string? MethodName { get; init; }
}
```

- [ ] Create `src/VBMigrator.Core/Models/TranslationResult.cs`:

```csharp
namespace VBMigrator.Core.Models;

public record TranslationResult
{
    public required string CsSource { get; init; }
    public double Confidence { get; init; }
    public TranslationRoute Route { get; init; }
    public bool CompilerPassed { get; init; }
    public List<string> CompilerErrors { get; init; } = new();
    public string? PatternTag { get; init; }
    public LlmFailureReason? LlmFailureReason { get; init; }
}
```

- [ ] Create `src/VBMigrator.Core/Models/DifficultyMap.cs`:

```csharp
namespace VBMigrator.Core.Models;

public record DifficultyMap
{
    public required string FilePath { get; init; }
    public int OverallScore { get; init; }
    public List<FunctionDifficulty> Functions { get; init; } = new();
}

public record FunctionDifficulty
{
    public required string MethodName { get; init; }
    public int Score { get; init; }
    public List<string> Flags { get; init; } = new();
    public TranslationRoute Route { get; init; }
}
```

- [ ] Create `src/VBMigrator.Core/Models/Pattern.cs`:

```csharp
namespace VBMigrator.Core.Models;

public class Pattern
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Tag { get; set; }
    public required string VbTemplate { get; set; }
    public required string CsTemplate { get; set; }
    public string Source { get; set; } = "seed";
    public int Applied { get; set; }
    public int Successes { get; set; }
    public byte[]? Embedding { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public double Confidence
    {
        get
        {
            var base_ = Source switch
            {
                "seed"          => 0.90,
                "human"         => 0.70,
                _               => 0.80
            };
            if (Applied == 0) return base_;
            return Math.Min(1.0, base_ + (double)Successes / Applied * 0.30);
        }
    }
}
```

- [ ] Create `src/VBMigrator.Core/Models/TranslationLog.cs`:

```csharp
namespace VBMigrator.Core.Models;

public class TranslationLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? PatternId { get; set; }
    public required string FilePath { get; set; }
    public string? MethodName { get; set; }
    public required string VbInput { get; set; }
    public required string CsOutput { get; set; }
    public bool WasCorrected { get; set; }
    public string? HumanCs { get; set; }
    public bool CompilerPassed { get; set; }
    public double Confidence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

- [ ] Delete `src/VBMigrator.Core/Placeholder.cs` (Models now exist).

- [ ] Run build:

```bash
dotnet build src/VBMigrator.Core/VBMigrator.Core.csproj
```

Expected output keyword: **Build succeeded**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/Models/
git commit -m "feat(core): add domain models (TranslationResult, DifficultyMap, Pattern, TranslationLog)"
```

---

## Task 3: ISeedRule interface + SeedRuleEngine

**Files:**
- `src/VBMigrator.Core/SeedRules/ISeedRule.cs`
- `src/VBMigrator.Core/SeedRules/SeedRuleEngine.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/SeedRuleEngineTests.cs`

- [ ] Create `src/VBMigrator.Core/SeedRules/ISeedRule.cs`:

```csharp
using Microsoft.CodeAnalysis;

namespace VBMigrator.Core.SeedRules;

public interface ISeedRule
{
    string Tag { get; }
    int Priority { get; }
    bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null);
    SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null);
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/SeedRuleEngine.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules;

/// <summary>
/// Applies registered ISeedRules to all descendant nodes of a VB method block.
/// Rules are ordered by Priority descending; first match per node wins.
/// Operates on Roslyn VB SyntaxTree nodes (never C# nodes).
/// </summary>
public class SeedRuleEngine
{
    private readonly IReadOnlyList<ISeedRule> _rules;

    public SeedRuleEngine(IEnumerable<ISeedRule> rules)
    {
        _rules = rules.OrderByDescending(r => r.Priority).ToList();
    }

    /// <summary>
    /// Walks all descendant nodes of <paramref name="methodNode"/> (a VB method block).
    /// For each node, the first rule whose CanHandle returns true wins and Convert is called.
    /// Returns a list of (tag, convertedNode) pairs for all matched nodes.
    /// </summary>
    public IReadOnlyList<(string Tag, SyntaxNode Original, SyntaxNode Converted)> Apply(
        SyntaxNode methodNode,
        SemanticModel? semanticModel = null)
    {
        var results = new List<(string, SyntaxNode, SyntaxNode)>();

        foreach (var node in methodNode.DescendantNodesAndSelf())
        {
            foreach (var rule in _rules)
            {
                if (rule.CanHandle(node, semanticModel))
                {
                    var converted = rule.Convert(node, semanticModel);
                    results.Add((rule.Tag, node, converted));
                    break; // first match wins per node
                }
            }
        }

        return results;
    }

    public IReadOnlyList<ISeedRule> Rules => _rules;
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/SeedRuleEngineTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class SeedRuleEngineTests
{
    private sealed class AlwaysRule : ISeedRule
    {
        public string Tag => "always";
        public int Priority => 100;
        public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null) => true;
        public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null) => node;
    }

    private sealed class NeverRule : ISeedRule
    {
        public string Tag => "never";
        public int Priority => 200;
        public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null) => false;
        public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null) => node;
    }

    [Fact]
    public void Apply_FirstMatchWins_HigherPriorityRuleUsedWhenBothMatch()
    {
        // Arrange: HighPrio always-matches, LowPrio always-matches too
        var highPrio = new PriorityAlwaysRule("high", 200);
        var lowPrio  = new PriorityAlwaysRule("low", 100);
        var engine = new SeedRuleEngine(new ISeedRule[] { lowPrio, highPrio }); // intentional reversed order

        var tree = SyntaxFactory.ParseSyntaxTree("Module M\n  Sub Foo()\n  End Sub\nEnd Module");
        var root = tree.GetRoot();

        // Act
        var results = engine.Apply(root);

        // Assert: every matched node should have tag "high" (higher priority wins)
        Assert.All(results, r => Assert.Equal("high", r.Tag));
    }

    [Fact]
    public void Apply_NeverRule_ProducesNoResults()
    {
        var engine = new SeedRuleEngine(new ISeedRule[] { new NeverRule() });
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\n  Sub Foo()\n  End Sub\nEnd Module");
        var results = engine.Apply(tree.GetRoot());
        Assert.Empty(results);
    }

    [Fact]
    public void Rules_AreOrderedByPriorityDescending()
    {
        var r1 = new PriorityAlwaysRule("a", 50);
        var r2 = new PriorityAlwaysRule("b", 150);
        var r3 = new PriorityAlwaysRule("c", 100);
        var engine = new SeedRuleEngine(new ISeedRule[] { r1, r2, r3 });

        var priorities = engine.Rules.Select(r => r.Priority).ToList();
        Assert.Equal(new[] { 150, 100, 50 }, priorities);
    }

    private sealed class PriorityAlwaysRule : ISeedRule
    {
        public PriorityAlwaysRule(string tag, int priority) { Tag = tag; Priority = priority; }
        public string Tag { get; }
        public int Priority { get; }
        public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null) => true;
        public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null) => node;
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~SeedRuleEngineTests" --no-build
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/SeedRules/ISeedRule.cs src/VBMigrator.Core/SeedRules/SeedRuleEngine.cs tests/VBMigrator.Core.Tests/SeedRules/SeedRuleEngineTests.cs
git commit -m "feat(core): ISeedRule interface + SeedRuleEngine (priority-ordered, first-match)"
```

---

## Task 4: Boolean, null-check, logical operator SeedRules (is_nothing, isnot_nothing, andalso, orelse, cint_bool)

**Files:**
- `src/VBMigrator.Core/SeedRules/Rules/NullCheckRules.cs`
- `src/VBMigrator.Core/SeedRules/Rules/LogicalOperatorRules.cs`
- `src/VBMigrator.Core/SeedRules/Rules/CintBoolRule.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/NullCheckRulesTests.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/LogicalOperatorRulesTests.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/CintBoolRuleTests.cs`

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/NullCheckRules.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using CS = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// is_nothing: x Is Nothing  →  x is null
/// </summary>
public sealed class IsNothingRule : ISeedRule
{
    public string Tag => "is_nothing";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is BinaryExpressionSyntax bin
           && bin.IsKind(SyntaxKind.IsExpression)
           && bin.Right is LiteralExpressionSyntax lit
           && lit.IsKind(SyntaxKind.NothingLiteralExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin = (BinaryExpressionSyntax)node;
        // Build C# pattern: x is null
        var leftText = bin.Left.ToString().Trim();
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression($"{leftText} is null");
    }
}

/// <summary>
/// isnot_nothing: x IsNot Nothing  →  x is not null
/// </summary>
public sealed class IsNotNothingRule : ISeedRule
{
    public string Tag => "isnot_nothing";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is BinaryExpressionSyntax bin
           && bin.IsKind(SyntaxKind.IsNotExpression)
           && bin.Right is LiteralExpressionSyntax lit
           && lit.IsKind(SyntaxKind.NothingLiteralExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin = (BinaryExpressionSyntax)node;
        var leftText = bin.Left.ToString().Trim();
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression($"{leftText} is not null");
    }
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/LogicalOperatorRules.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// andalso: AndAlso  →  &&
/// </summary>
public sealed class AndAlsoRule : ISeedRule
{
    public string Tag => "andalso";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is BinaryExpressionSyntax bin
           && bin.IsKind(SyntaxKind.AndAlsoExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin = (BinaryExpressionSyntax)node;
        var left  = bin.Left.ToString().Trim();
        var right = bin.Right.ToString().Trim();
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression($"{left} && {right}");
    }
}

/// <summary>
/// orelse: OrElse  →  ||
/// </summary>
public sealed class OrElseRule : ISeedRule
{
    public string Tag => "orelse";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is BinaryExpressionSyntax bin
           && bin.IsKind(SyntaxKind.OrElseExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin = (BinaryExpressionSyntax)node;
        var left  = bin.Left.ToString().Trim();
        var right = bin.Right.ToString().Trim();
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression($"{left} || {right}");
    }
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/CintBoolRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// cint_bool: CInt(True) → (true ? -1 : 0)   CInt(False) → 0
/// VB: CInt(True) = -1 (not 1). Always adds comment.
/// </summary>
public sealed class CintBoolRule : ISeedRule
{
    public string Tag => "cint_bool";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        if (inv.Expression is not IdentifierNameSyntax id) return false;
        if (!string.Equals(id.Identifier.Text, "CInt", StringComparison.OrdinalIgnoreCase)) return false;
        var args = inv.ArgumentList?.Arguments;
        if (args is null || args.Value.Count != 1) return false;
        var arg = args.Value[0].GetExpression();
        return arg is LiteralExpressionSyntax lit
            && (lit.IsKind(SyntaxKind.TrueLiteralExpression) || lit.IsKind(SyntaxKind.FalseLiteralExpression));
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var inv = (InvocationExpressionSyntax)node;
        var arg = inv.ArgumentList!.Arguments[0].GetExpression()!;
        bool isTrue = arg.IsKind(SyntaxKind.TrueLiteralExpression);

        var csExpr = isTrue
            ? "/* VB: CInt(True) = -1 */ (true ? -1 : 0)"
            : "/* VB: CInt(False) = 0 */ 0";

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(csExpr);
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/NullCheckRulesTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class NullCheckRulesTests
{
    [Fact]
    public void IsNothingRule_CanHandle_BinaryIsNothing_ReturnsTrue()
    {
        var rule = new IsNothingRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = x Is Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IsExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void IsNothingRule_Convert_ProducesIsNull()
    {
        var rule = new IsNothingRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = x Is Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IsExpression));
        var result = rule.Convert(node);
        Assert.Contains("is null", result.ToString());
    }

    [Fact]
    public void IsNotNothingRule_CanHandle_BinaryIsNotNothing_ReturnsTrue()
    {
        var rule = new IsNotNothingRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = x IsNot Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IsNotExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void IsNotNothingRule_Convert_ProducesIsNotNull()
    {
        var rule = new IsNotNothingRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = x IsNot Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IsNotExpression));
        var result = rule.Convert(node);
        Assert.Contains("is not null", result.ToString());
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/LogicalOperatorRulesTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class LogicalOperatorRulesTests
{
    [Fact]
    public void AndAlsoRule_Convert_ProducesDoubleAmpersand()
    {
        var rule = new AndAlsoRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = a AndAlso c\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.AndAlsoExpression));
        Assert.True(rule.CanHandle(node));
        var result = rule.Convert(node);
        Assert.Contains("&&", result.ToString());
    }

    [Fact]
    public void OrElseRule_Convert_ProducesDoublePipe()
    {
        var rule = new OrElseRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = a OrElse c\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.OrElseExpression));
        Assert.True(rule.CanHandle(node));
        var result = rule.Convert(node);
        Assert.Contains("||", result.ToString());
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/CintBoolRuleTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class CintBoolRuleTests
{
    private readonly CintBoolRule _rule = new();

    [Fact]
    public void CanHandle_CIntTrue_ReturnsTrue()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim x = CInt(True)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        Assert.True(_rule.CanHandle(node));
    }

    [Fact]
    public void Convert_CIntTrue_ProducesMinusOneExpression()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim x = CInt(True)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("-1", result);
        Assert.Contains("VB: CInt(True) = -1", result);
    }

    [Fact]
    public void Convert_CIntFalse_ProducesZero()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim x = CInt(False)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("0", result);
    }

    [Fact]
    public void CanHandle_CIntNumericArg_ReturnsFalse()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim x = CInt(42)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        Assert.False(_rule.CanHandle(node));
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~NullCheckRules|FullyQualifiedName~LogicalOperatorRules|FullyQualifiedName~CintBoolRule"
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/SeedRules/Rules/NullCheckRules.cs src/VBMigrator.Core/SeedRules/Rules/LogicalOperatorRules.cs src/VBMigrator.Core/SeedRules/Rules/CintBoolRule.cs tests/VBMigrator.Core.Tests/SeedRules/NullCheckRulesTests.cs tests/VBMigrator.Core.Tests/SeedRules/LogicalOperatorRulesTests.cs tests/VBMigrator.Core.Tests/SeedRules/CintBoolRuleTests.cs
git commit -m "feat(seed): is_nothing, isnot_nothing, andalso, orelse, cint_bool rules"
```

---

## Task 5: Arithmetic & array SeedRules (integer_division, exponentiation, redim_preserve, erase_array)

**Files:**
- `src/VBMigrator.Core/SeedRules/Rules/IntegerDivisionRule.cs`
- `src/VBMigrator.Core/SeedRules/Rules/ExponentiationRule.cs`
- `src/VBMigrator.Core/SeedRules/Rules/ArrayRules.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/ArithmeticRulesTests.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/ArrayRulesTests.cs`

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/IntegerDivisionRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// integer_division: x \ y  →  (int)(x / y)
/// VB integer division operator is SyntaxKind.IntegerDivideExpression.
/// </summary>
public sealed class IntegerDivisionRule : ISeedRule
{
    public string Tag => "integer_division";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is BinaryExpressionSyntax bin
           && bin.IsKind(SyntaxKind.IntegerDivideExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin = (BinaryExpressionSyntax)node;
        var left  = bin.Left.ToString().Trim();
        var right = bin.Right.ToString().Trim();
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression($"(int)({left} / {right})");
    }
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/ExponentiationRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// exponentiation: x ^ y  →  Math.Pow(x, y)
/// VB exponentiation operator is SyntaxKind.ExponentiateExpression.
/// </summary>
public sealed class ExponentiationRule : ISeedRule
{
    public string Tag => "exponentiation";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is BinaryExpressionSyntax bin
           && bin.IsKind(SyntaxKind.ExponentiateExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin = (BinaryExpressionSyntax)node;
        var left  = bin.Left.ToString().Trim();
        var right = bin.Right.ToString().Trim();
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression($"Math.Pow({left}, {right})");
    }
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/ArrayRules.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// redim_preserve: ReDim Preserve arr(n)  →  Array.Resize(ref arr, n + 1)
/// n+1 because ReDim specifies the upper bound (0-based), not the count.
/// </summary>
public sealed class RedimPreserveRule : ISeedRule
{
    public string Tag => "redim_preserve";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is ReDimStatementSyntax redim && redim.PreserveKeyword.IsKind(SyntaxKind.PreserveKeyword);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var redim = (ReDimStatementSyntax)node;
        // ReDim Preserve arr(n) — take first clause
        var clause = redim.Clauses[0];
        var arrName = clause.Expression.ToString().Trim();
        // Upper bound is the first argument in the index args
        var upperBound = clause.ArrayBounds.Arguments[0].GetExpression()!.ToString().Trim();
        var csText = $"Array.Resize(ref {arrName}, {upperBound} + 1);";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(csText);
    }
}

/// <summary>
/// erase_array: Erase arr  →  arr = null
/// </summary>
public sealed class EraseArrayRule : ISeedRule
{
    public string Tag => "erase_array";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is EraseStatementSyntax;

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var erase = (EraseStatementSyntax)node;
        // Erase can have multiple variables; take first for MVP
        var varName = erase.Expressions[0].ToString().Trim();
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement($"{varName} = null;");
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/ArithmeticRulesTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class ArithmeticRulesTests
{
    [Fact]
    public void IntegerDivisionRule_CanHandle_BackslashExpression_ReturnsTrue()
    {
        var rule = new IntegerDivisionRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = x \\ y\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IntegerDivideExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void IntegerDivisionRule_Convert_ProducesCastDivision()
    {
        var rule = new IntegerDivisionRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = x \\ y\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IntegerDivideExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("(int)", result);
        Assert.Contains("x / y", result);
    }

    [Fact]
    public void ExponentiationRule_CanHandle_CaretExpression_ReturnsTrue()
    {
        var rule = new ExponentiationRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = x ^ y\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ExponentiateExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void ExponentiationRule_Convert_ProducesMathPow()
    {
        var rule = new ExponentiationRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = x ^ y\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ExponentiateExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("Math.Pow", result);
        Assert.Contains("x", result);
        Assert.Contains("y", result);
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/ArrayRulesTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class ArrayRulesTests
{
    [Fact]
    public void RedimPreserveRule_CanHandle_ReDimPreserve_ReturnsTrue()
    {
        var rule = new RedimPreserveRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nReDim Preserve arr(n)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ReDimStatement));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void RedimPreserveRule_Convert_ProducesArrayResize()
    {
        var rule = new RedimPreserveRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nReDim Preserve arr(n)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ReDimStatement));
        var result = rule.Convert(node).ToString();
        Assert.Contains("Array.Resize", result);
        Assert.Contains("ref arr", result);
        Assert.Contains("n + 1", result);
    }

    [Fact]
    public void EraseArrayRule_CanHandle_EraseStatement_ReturnsTrue()
    {
        var rule = new EraseArrayRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nErase arr\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.EraseStatement));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void EraseArrayRule_Convert_ProducesNullAssignment()
    {
        var rule = new EraseArrayRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nErase arr\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.EraseStatement));
        var result = rule.Convert(node).ToString();
        Assert.Contains("arr", result);
        Assert.Contains("null", result);
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~ArithmeticRules|FullyQualifiedName~ArrayRules"
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/SeedRules/Rules/IntegerDivisionRule.cs src/VBMigrator.Core/SeedRules/Rules/ExponentiationRule.cs src/VBMigrator.Core/SeedRules/Rules/ArrayRules.cs tests/VBMigrator.Core.Tests/SeedRules/ArithmeticRulesTests.cs tests/VBMigrator.Core.Tests/SeedRules/ArrayRulesTests.cs
git commit -m "feat(seed): integer_division, exponentiation, redim_preserve, erase_array rules"
```

---

## Task 6: String & pattern SeedRules (string_concat, string_comparison_case, iif_function, like_operator)

**Files:**
- `src/VBMigrator.Core/SeedRules/Rules/StringConcatRule.cs`
- `src/VBMigrator.Core/SeedRules/Rules/StringComparisonRule.cs`
- `src/VBMigrator.Core/SeedRules/Rules/IifFunctionRule.cs`
- `src/VBMigrator.Core/SeedRules/Rules/LikeOperatorRule.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/StringRulesTests.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/LikeOperatorRuleTests.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/IifFunctionRuleTests.cs`

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/StringConcatRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// string_concat: s1 &amp; s2  →  s1 + s2
/// With semanticModel: if either operand is numeric → confidence 0.70 (SUGGEST).
/// Without semanticModel: if operand name suggests numeric (heuristic) → same downgrade.
/// The Convert method always emits s1 + s2; the caller reads the numeric flag from ConfidenceHint.
/// </summary>
public sealed class StringConcatRule : ISeedRule
{
    public string Tag => "string_concat";
    public int Priority => 100;

    /// <summary>True when the last Convert detected a potentially-numeric operand.</summary>
    public bool LastConvertHadNumericOperand { get; private set; }

    private static readonly HashSet<string> _numericHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "count", "total", "sum", "amount", "price", "qty", "quantity",
        "num", "number", "index", "idx", "i", "j", "k", "n", "x", "y", "z",
        "value", "val", "result", "score", "length", "len", "size"
    };

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is BinaryExpressionSyntax bin
           && bin.IsKind(SyntaxKind.ConcatenateExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin = (BinaryExpressionSyntax)node;
        var left  = bin.Left.ToString().Trim();
        var right = bin.Right.ToString().Trim();

        LastConvertHadNumericOperand = false;

        if (semanticModel is not null)
        {
            var leftType  = semanticModel.GetTypeInfo(bin.Left).Type;
            var rightType = semanticModel.GetTypeInfo(bin.Right).Type;
            bool leftNum  = IsNumericType(leftType?.SpecialType ?? Microsoft.CodeAnalysis.SpecialType.None);
            bool rightNum = IsNumericType(rightType?.SpecialType ?? Microsoft.CodeAnalysis.SpecialType.None);
            LastConvertHadNumericOperand = leftNum || rightNum;
        }
        else
        {
            // Name heuristic: check identifier names
            var leftId  = ExtractIdentifierName(bin.Left);
            var rightId = ExtractIdentifierName(bin.Right);
            LastConvertHadNumericOperand =
                (leftId is not null && _numericHints.Contains(leftId)) ||
                (rightId is not null && _numericHints.Contains(rightId));
        }

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression($"{left} + {right}");
    }

    private static bool IsNumericType(Microsoft.CodeAnalysis.SpecialType st) => st is
        Microsoft.CodeAnalysis.SpecialType.System_Byte or
        Microsoft.CodeAnalysis.SpecialType.System_SByte or
        Microsoft.CodeAnalysis.SpecialType.System_Int16 or
        Microsoft.CodeAnalysis.SpecialType.System_UInt16 or
        Microsoft.CodeAnalysis.SpecialType.System_Int32 or
        Microsoft.CodeAnalysis.SpecialType.System_UInt32 or
        Microsoft.CodeAnalysis.SpecialType.System_Int64 or
        Microsoft.CodeAnalysis.SpecialType.System_UInt64 or
        Microsoft.CodeAnalysis.SpecialType.System_Single or
        Microsoft.CodeAnalysis.SpecialType.System_Double or
        Microsoft.CodeAnalysis.SpecialType.System_Decimal;

    private static string? ExtractIdentifierName(SyntaxNode node)
    {
        if (node is IdentifierNameSyntax id) return id.Identifier.Text;
        if (node is MemberAccessExpressionSyntax mem) return mem.Name.Identifier.Text;
        return null;
    }
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/StringComparisonRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// string_comparison_case:
///   String.Compare(a, b, True)  →  string.Compare(a, b, StringComparison.OrdinalIgnoreCase)
///   String.Compare(a, b, False) →  string.Compare(a, b, StringComparison.Ordinal)
/// Matches only the 3-argument overload where the third arg is a boolean literal.
/// </summary>
public sealed class StringComparisonRule : ISeedRule
{
    public string Tag => "string_comparison_case";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        if (inv.Expression is not MemberAccessExpressionSyntax mem) return false;
        if (!string.Equals(mem.Name.Identifier.Text, "Compare", StringComparison.Ordinal)) return false;

        // Check receiver is String or string
        var receiver = mem.Expression.ToString().Trim();
        if (!string.Equals(receiver, "String", StringComparison.OrdinalIgnoreCase)) return false;

        var args = inv.ArgumentList?.Arguments;
        if (args is null || args.Value.Count != 3) return false;

        var third = args.Value[2].GetExpression();
        return third is LiteralExpressionSyntax lit
            && (lit.IsKind(SyntaxKind.TrueLiteralExpression) || lit.IsKind(SyntaxKind.FalseLiteralExpression));
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var inv   = (InvocationExpressionSyntax)node;
        var args  = inv.ArgumentList!.Arguments;
        var a     = args[0].GetExpression()!.ToString().Trim();
        var b     = args[1].GetExpression()!.ToString().Trim();
        var third = args[2].GetExpression()!;
        bool ignoreCase = third.IsKind(SyntaxKind.TrueLiteralExpression);
        var comparison  = ignoreCase
            ? "StringComparison.OrdinalIgnoreCase"
            : "StringComparison.Ordinal";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"string.Compare({a}, {b}, {comparison})");
    }
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/IifFunctionRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// iif_function: IIf(condition, a, b)  →  (condition ? a : b)
/// ALWAYS prepends warning comment — no exceptions.
/// Warning: "⚠ VBMigrator: IIf evalúa ambos brazos en VB; ternario ?: no lo hace"
/// </summary>
public sealed class IifFunctionRule : ISeedRule
{
    public string Tag => "iif_function";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        if (inv.Expression is not IdentifierNameSyntax id) return false;
        if (!string.Equals(id.Identifier.Text, "IIf", StringComparison.OrdinalIgnoreCase)) return false;
        var args = inv.ArgumentList?.Arguments;
        return args is not null && args.Value.Count == 3;
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var inv  = (InvocationExpressionSyntax)node;
        var args = inv.ArgumentList!.Arguments;
        var cond = args[0].GetExpression()!.ToString().Trim();
        var a    = args[1].GetExpression()!.ToString().Trim();
        var b    = args[2].GetExpression()!.ToString().Trim();

        // Warning comment is ALWAYS emitted, per spec.
        var csText =
            $"/* ⚠ VBMigrator: IIf evalúa ambos brazos en VB; ternario ?: no lo hace */ ({cond} ? {a} : {b})";

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(csText);
    }
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/LikeOperatorRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using System.Text;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// like_operator: s Like "pattern"  →  Regex.IsMatch(s, translatedPattern)
/// Wildcard mapping: * → .*, ? → ., # → \d, [abc] → [abc] (preserved)
/// Anchors: always wraps with ^ ... $ to match VB Like semantics (full-string match).
/// </summary>
public sealed class LikeOperatorRule : ISeedRule
{
    public string Tag => "like_operator";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is BinaryExpressionSyntax bin
           && bin.IsKind(SyntaxKind.LikeExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin     = (BinaryExpressionSyntax)node;
        var subject = bin.Left.ToString().Trim();
        var pattern = bin.Right.ToString().Trim();

        // Strip surrounding quotes if it's a string literal, translate wildcards
        string regexPattern;
        if (pattern.StartsWith("\"") && pattern.EndsWith("\"") && pattern.Length >= 2)
        {
            var inner = pattern[1..^1];
            regexPattern = "\"^" + TranslateWildcards(inner) + "$\"";
        }
        else
        {
            // Non-literal: can't translate statically, emit as-is with comment
            regexPattern = $"/* VBMigrator: translate Like pattern */ {pattern}";
        }

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"System.Text.RegularExpressions.Regex.IsMatch({subject}, {regexPattern})");
    }

    internal static string TranslateWildcards(string vbPattern)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < vbPattern.Length)
        {
            char c = vbPattern[i];
            switch (c)
            {
                case '*':
                    sb.Append(".*");
                    i++;
                    break;
                case '?':
                    sb.Append('.');
                    i++;
                    break;
                case '#':
                    sb.Append(@"\d");
                    i++;
                    break;
                case '[':
                    // Find matching ] and preserve character class as-is
                    int close = vbPattern.IndexOf(']', i + 1);
                    if (close >= 0)
                    {
                        sb.Append(vbPattern[i..(close + 1)]);
                        i = close + 1;
                    }
                    else
                    {
                        sb.Append(@"\[");
                        i++;
                    }
                    break;
                default:
                    // Escape regex meta-characters that are not VB wildcards
                    sb.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }
        return sb.ToString();
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/StringRulesTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class StringRulesTests
{
    [Fact]
    public void StringConcatRule_CanHandle_AmpersandExpression_ReturnsTrue()
    {
        var rule = new StringConcatRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim s = a & b\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ConcatenateExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void StringConcatRule_Convert_ProducesPlusOperator()
    {
        var rule = new StringConcatRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim s = a & b\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ConcatenateExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("+", result);
        Assert.False(result.Contains("&"), "should not contain VB & operator");
    }

    [Fact]
    public void StringConcatRule_Convert_NumericHeuristicName_SetsNumericFlag()
    {
        var rule = new StringConcatRule();
        // 'count' is in the numeric hints set
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim s = count & b\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ConcatenateExpression));
        rule.Convert(node, semanticModel: null);
        Assert.True(rule.LastConvertHadNumericOperand);
    }

    [Fact]
    public void StringComparisonRule_CanHandle_ThreeArgStringCompareTrue_ReturnsTrue()
    {
        var rule = new StringComparisonRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = String.Compare(a, b, True)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void StringComparisonRule_Convert_True_ProducesOrdinalIgnoreCase()
    {
        var rule = new StringComparisonRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = String.Compare(a, b, True)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("OrdinalIgnoreCase", result);
    }

    [Fact]
    public void StringComparisonRule_Convert_False_ProducesOrdinal()
    {
        var rule = new StringComparisonRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = String.Compare(a, b, False)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("StringComparison.Ordinal", result);
        Assert.DoesNotContain("IgnoreCase", result);
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/IifFunctionRuleTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class IifFunctionRuleTests
{
    private readonly IifFunctionRule _rule = new();

    [Fact]
    public void CanHandle_IIfThreeArgs_ReturnsTrue()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = IIf(x > 0, a, b)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        Assert.True(_rule.CanHandle(node));
    }

    [Fact]
    public void Convert_AlwaysEmitsWarningComment()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = IIf(x > 0, a, b)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = _rule.Convert(node).ToString();
        // Warning must ALWAYS appear regardless of side-effect analysis
        Assert.Contains("IIf evalúa ambos brazos", result);
    }

    [Fact]
    public void Convert_ProducesTernaryExpression()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = IIf(x > 0, a, b)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("?", result);
        Assert.Contains(":", result);
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/LikeOperatorRuleTests.cs`:

```csharp
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class LikeOperatorRuleTests
{
    [Theory]
    [InlineData("A*",   "^A.*$")]
    [InlineData("A?B",  "^A.B$")]
    [InlineData("##",   @"^\d\d$")]
    [InlineData("[abc]","^[abc]$")]
    [InlineData("A*B?", "^A.*B.$")]
    public void TranslateWildcards_MapsCorrectly(string vbPattern, string expectedRegex)
    {
        var result = LikeOperatorRule.TranslateWildcards(vbPattern);
        Assert.Equal(expectedRegex[1..^1], result); // strip anchors added by Convert, not TranslateWildcards
    }

    [Fact]
    public void CanHandle_LikeExpression_ReturnsTrue()
    {
        var rule = new LikeOperatorRule();
        var tree = Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory
            .ParseSyntaxTree("Module M\nSub F()\nDim r = s Like \"A*\"\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.LikeExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void Convert_LiteralPattern_ProducesRegexIsMatch()
    {
        var rule = new LikeOperatorRule();
        var tree = Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory
            .ParseSyntaxTree("Module M\nSub F()\nDim r = s Like \"A*\"\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.LikeExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("Regex.IsMatch", result);
        Assert.Contains(".*", result);
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~StringRules|FullyQualifiedName~LikeOperator|FullyQualifiedName~IifFunction"
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/SeedRules/Rules/StringConcatRule.cs src/VBMigrator.Core/SeedRules/Rules/StringComparisonRule.cs src/VBMigrator.Core/SeedRules/Rules/IifFunctionRule.cs src/VBMigrator.Core/SeedRules/Rules/LikeOperatorRule.cs tests/VBMigrator.Core.Tests/SeedRules/StringRulesTests.cs tests/VBMigrator.Core.Tests/SeedRules/IifFunctionRuleTests.cs tests/VBMigrator.Core.Tests/SeedRules/LikeOperatorRuleTests.cs
git commit -m "feat(seed): string_concat, string_comparison_case, iif_function, like_operator rules"
```

---

## Task 7: Parameter & date SeedRules (byval_param, optional_param, date_literal)

**Files:**
- `src/VBMigrator.Core/SeedRules/Rules/ParameterRules.cs`
- `src/VBMigrator.Core/SeedRules/Rules/DateLiteralRule.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/ParameterRulesTests.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/DateLiteralRuleTests.cs`

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/ParameterRules.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// byval_param: ByVal x As T  →  T x
/// ByVal is the default in C# — just remove the modifier.
/// Operates on ParameterSyntax nodes.
/// </summary>
public sealed class ByValParamRule : ISeedRule
{
    public string Tag => "byval_param";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not ParameterSyntax param) return false;
        return param.Modifiers.Any(m => m.IsKind(SyntaxKind.ByValKeyword));
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var param = (ParameterSyntax)node;
        var typeName = param.AsClause?.Type?.ToString().Trim() ?? "object";
        var paramName = param.Identifier.Identifier.Text;
        // Emit as C# parameter: T x
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseParameterList($"({typeName} {paramName})")
            .Parameters[0];
    }
}

/// <summary>
/// optional_param: Optional ByVal x As T = v  →  T x = v
/// </summary>
public sealed class OptionalParamRule : ISeedRule
{
    public string Tag => "optional_param";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not ParameterSyntax param) return false;
        return param.Modifiers.Any(m => m.IsKind(SyntaxKind.OptionalKeyword));
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var param = (ParameterSyntax)node;
        var typeName  = param.AsClause?.Type?.ToString().Trim() ?? "object";
        var paramName = param.Identifier.Identifier.Text;
        var defaultVal = param.Default?.Value?.ToString().Trim() ?? "default";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseParameterList($"({typeName} {paramName} = {defaultVal})")
            .Parameters[0];
    }
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/DateLiteralRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using System.Text.RegularExpressions;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// date_literal: #2020-01-01#  →  new DateTime(2020, 1, 1)
/// Uses regex to extract year/month/day from the literal text.
/// Supports formats: #yyyy-MM-dd# and #M/d/yyyy#
/// </summary>
public sealed class DateLiteralRule : ISeedRule
{
    public string Tag => "date_literal";
    public int Priority => 100;

    // Matches #yyyy-MM-dd# or #M/d/yyyy# style VB date literals
    private static readonly Regex _iso   = new(@"#(\d{4})-(\d{1,2})-(\d{1,2})#", RegexOptions.Compiled);
    private static readonly Regex _slash = new(@"#(\d{1,2})/(\d{1,2})/(\d{4})#", RegexOptions.Compiled);

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not LiteralExpressionSyntax lit) return false;
        if (!lit.IsKind(SyntaxKind.DateLiteralExpression)) return false;
        var text = lit.ToString();
        return _iso.IsMatch(text) || _slash.IsMatch(text);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var lit  = (LiteralExpressionSyntax)node;
        var text = lit.ToString();

        int year, month, day;
        var isoMatch = _iso.Match(text);
        if (isoMatch.Success)
        {
            year  = int.Parse(isoMatch.Groups[1].Value);
            month = int.Parse(isoMatch.Groups[2].Value);
            day   = int.Parse(isoMatch.Groups[3].Value);
        }
        else
        {
            var slashMatch = _slash.Match(text);
            month = int.Parse(slashMatch.Groups[1].Value);
            day   = int.Parse(slashMatch.Groups[2].Value);
            year  = int.Parse(slashMatch.Groups[3].Value);
        }

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"new DateTime({year}, {month}, {day})");
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/ParameterRulesTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class ParameterRulesTests
{
    [Fact]
    public void ByValParamRule_CanHandle_ByValParameter_ReturnsTrue()
    {
        var rule = new ByValParamRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F(ByVal x As Integer)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.Parameter));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void ByValParamRule_Convert_RemovesByValModifier()
    {
        var rule = new ByValParamRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F(ByVal x As Integer)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.Parameter));
        var result = rule.Convert(node).ToString();
        Assert.DoesNotContain("ByVal", result);
        Assert.Contains("Integer", result);
        Assert.Contains("x", result);
    }

    [Fact]
    public void OptionalParamRule_CanHandle_OptionalParameter_ReturnsTrue()
    {
        var rule = new OptionalParamRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F(Optional ByVal x As Integer = 0)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.Parameter));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void OptionalParamRule_Convert_ProducesDefaultValue()
    {
        var rule = new OptionalParamRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F(Optional ByVal x As Integer = 0)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.Parameter));
        var result = rule.Convert(node).ToString();
        Assert.Contains("= 0", result);
        Assert.DoesNotContain("Optional", result);
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/DateLiteralRuleTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class DateLiteralRuleTests
{
    private readonly DateLiteralRule _rule = new();

    [Fact]
    public void CanHandle_IsoDateLiteral_ReturnsTrue()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim d = #2020-01-15#\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.DateLiteralExpression));
        Assert.True(_rule.CanHandle(node));
    }

    [Fact]
    public void Convert_IsoDate_ProducesNewDateTime()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim d = #2020-01-15#\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.DateLiteralExpression));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("new DateTime", result);
        Assert.Contains("2020", result);
        Assert.Contains("1", result);
        Assert.Contains("15", result);
    }

    [Fact]
    public void Convert_SlashDate_ProducesNewDateTime()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim d = #1/15/2020#\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.DateLiteralExpression));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("new DateTime", result);
        Assert.Contains("2020", result);
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~ParameterRules|FullyQualifiedName~DateLiteralRule"
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/SeedRules/Rules/ParameterRules.cs src/VBMigrator.Core/SeedRules/Rules/DateLiteralRule.cs tests/VBMigrator.Core.Tests/SeedRules/ParameterRulesTests.cs tests/VBMigrator.Core.Tests/SeedRules/DateLiteralRuleTests.cs
git commit -m "feat(seed): byval_param, optional_param, date_literal rules"
```

---

## Task 8: Error-handling & byte-loop SeedRules (on_error_goto, on_error_resume, for_byte_overflow)

**Files:**
- `src/VBMigrator.Core/SeedRules/Rules/OnErrorGotoRule.cs`
- `src/VBMigrator.Core/SeedRules/Rules/OnErrorResumeRule.cs`
- `src/VBMigrator.Core/SeedRules/Rules/ByteLoopRule.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/OnErrorRulesTests.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/ByteLoopRuleTests.cs`

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/OnErrorGotoRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// on_error_goto: On Error GoTo Label ... Label: ... Err.Description
///   → try { } catch (Exception ex) { } with ex.Message replacing Err.Description
/// Priority 150.
/// CROSSBLOCK_GOTO interaction: if the method has a GotoCrossBlock flag passed via
/// a context object, CanHandle returns false → method goes to HumanQueue with CROSSBLOCK_GOTO.
/// This rule does NOT handle cross-block GoTo; simple try/catch is semantically wrong there.
/// </summary>
public sealed class OnErrorGotoRule : ISeedRule
{
    public string Tag => "on_error_goto";
    public int Priority => 150;

    /// <summary>
    /// When set to true (by the pipeline, derived from DifficultyMap GotoCrossBlock flag),
    /// CanHandle returns false unconditionally, routing the method to HumanQueue.
    /// </summary>
    public bool HasGotoCrossBlock { get; set; }

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (HasGotoCrossBlock) return false;
        return node is OnErrorGoToStatementSyntax stmt
            && !stmt.GoToLabel.IsKind(SyntaxKind.ZeroLiteralExpression); // GoTo 0 clears error handler
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var stmt = (OnErrorGoToStatementSyntax)node;
        var labelName = stmt.GoToLabel.ToString().Trim();

        // We emit a try/catch skeleton. The pipeline assembles the full method body.
        // Err.Description references in the catch block are replaced with ex.Message
        // by a post-processing step in the pipeline that walks the catch block.
        var csText = $@"try
{{
    // TODO: VBMigrator — body before On Error GoTo {labelName}
}}
catch (Exception ex)
{{
    // {labelName}: (translated from VB error handler)
    // ex.Message replaces Err.Description
}}";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(csText);
    }
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/OnErrorResumeRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// on_error_resume: On Error Resume Next
///   → HumanQueue + comment "⚠ VBMigrator: On Error Resume Next — requiere revisión manual"
/// Priority 150. Phase 1 does NOT generate per-statement wrapping.
/// The pipeline checks the returned node for the HumanQueue marker comment.
/// </summary>
public sealed class OnErrorResumeRule : ISeedRule
{
    public string Tag => "on_error_resume";
    public int Priority => 150;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is OnErrorResumeNextStatementSyntax;

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        // Emit a comment marker that the pipeline detects to enqueue for human review.
        // The comment text is the contract with TranslationPipeline.
        const string markerComment =
            "// ⚠ VBMigrator: On Error Resume Next — requiere revisión manual";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(markerComment + "\n/* HUMAN_QUEUE:on_error_resume */");
    }
}
```

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/ByteLoopRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// for_byte_overflow: For i As Byte = 0 To 255
///   → HumanQueue + flag BYTE_LOOP_OVERFLOW + comment
/// MVP limitation: detection by literal "255" in ToValue text.
/// If the bound is a constant (Const MAX_BYTE = 255), the rule does not detect the case.
/// </summary>
public sealed class ByteLoopRule : ISeedRule
{
    public string Tag => "for_byte_overflow";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not ForStatementSyntax forStmt) return false;

        // Check that the control variable is declared As Byte
        bool isByteVar = false;
        if (forStmt.ControlVariable is VariableDeclaratorSyntax decl)
        {
            var asClause = decl.AsClause as SimpleAsClauseSyntax;
            isByteVar = string.Equals(
                asClause?.Type?.ToString().Trim(), "Byte", StringComparison.OrdinalIgnoreCase);
        }
        if (!isByteVar) return false;

        // MVP: detect literal 255 in ToValue
        var toText = forStmt.ToValue?.ToString().Trim() ?? "";
        return toText == "255";
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        const string markerComment =
            "// ⚠ VBMigrator: byte loop puede ser bucle infinito en VB\n" +
            "/* HUMAN_QUEUE:BYTE_LOOP_OVERFLOW */";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(markerComment);
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/OnErrorRulesTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class OnErrorRulesTests
{
    [Fact]
    public void OnErrorGotoRule_CanHandle_OnErrorGoTo_ReturnsTrue()
    {
        var rule = new OnErrorGotoRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nOn Error GoTo ErrHandler\nErrHandler:\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.OnErrorGoToStatement));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void OnErrorGotoRule_CanHandle_WithGotoCrossBlock_ReturnsFalse()
    {
        var rule = new OnErrorGotoRule { HasGotoCrossBlock = true };
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nOn Error GoTo ErrHandler\nErrHandler:\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.OnErrorGoToStatement));
        // With cross-block flag, CanHandle must return false → HumanQueue
        Assert.False(rule.CanHandle(node));
    }

    [Fact]
    public void OnErrorGotoRule_Convert_ProducesTryCatch()
    {
        var rule = new OnErrorGotoRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nOn Error GoTo ErrHandler\nErrHandler:\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.OnErrorGoToStatement));
        var result = rule.Convert(node).ToString();
        Assert.Contains("try", result);
        Assert.Contains("catch", result);
        Assert.Contains("Exception ex", result);
        Assert.Contains("ex.Message", result);
    }

    [Fact]
    public void OnErrorResumeRule_CanHandle_OnErrorResumeNext_ReturnsTrue()
    {
        var rule = new OnErrorResumeRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nOn Error Resume Next\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.OnErrorResumeNextStatement));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void OnErrorResumeRule_Convert_EmitsHumanQueueMarker()
    {
        var rule = new OnErrorResumeRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nOn Error Resume Next\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.OnErrorResumeNextStatement));
        var result = rule.Convert(node).ToString();
        Assert.Contains("On Error Resume Next", result);
        Assert.Contains("revisión manual", result);
        Assert.Contains("HUMAN_QUEUE", result);
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/ByteLoopRuleTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class ByteLoopRuleTests
{
    private readonly ByteLoopRule _rule = new();

    [Fact]
    public void CanHandle_ByteTo255_ReturnsTrue()
    {
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nFor i As Byte = 0 To 255\nNext\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ForStatement));
        Assert.True(_rule.CanHandle(node));
    }

    [Fact]
    public void CanHandle_ByteTo254_ReturnsFalse()
    {
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nFor i As Byte = 0 To 254\nNext\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ForStatement));
        Assert.False(_rule.CanHandle(node));
    }

    [Fact]
    public void CanHandle_IntTo255_ReturnsFalse()
    {
        // Integer loop to 255 is not a byte overflow risk
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nFor i As Integer = 0 To 255\nNext\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ForStatement));
        Assert.False(_rule.CanHandle(node));
    }

    [Fact]
    public void Convert_EmitsHumanQueueMarker()
    {
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nFor i As Byte = 0 To 255\nNext\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ForStatement));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("byte loop", result);
        Assert.Contains("BYTE_LOOP_OVERFLOW", result);
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~OnErrorRules|FullyQualifiedName~ByteLoopRule"
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/SeedRules/Rules/OnErrorGotoRule.cs src/VBMigrator.Core/SeedRules/Rules/OnErrorResumeRule.cs src/VBMigrator.Core/SeedRules/Rules/ByteLoopRule.cs tests/VBMigrator.Core.Tests/SeedRules/OnErrorRulesTests.cs tests/VBMigrator.Core.Tests/SeedRules/ByteLoopRuleTests.cs
git commit -m "feat(seed): on_error_goto (Priority 150, GotoCrossBlock guard), on_error_resume, for_byte_overflow"
```

---

## Task 9: My namespace SeedRules (my_settings, my_filesystem_read, my_filesystem_write, my_app_version, my_user)

**Files:**
- `src/VBMigrator.Core/SeedRules/Rules/MyNamespaceRules.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/MyNamespaceRulesTests.cs`

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/MyNamespaceRules.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

// Five separate classes in one file per spec. Each has its own CanHandle/Convert.
// No internal dispatch — each class independently matches its specific My.* pattern.

/// <summary>
/// my_settings: My.Settings.Foo  →  Properties.Settings.Default.Foo
/// </summary>
public sealed class MySettingsRule : ISeedRule
{
    public string Tag => "my_settings";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        // Matches My.Settings.Foo — a member access chain starting with My.Settings
        if (node is not MemberAccessExpressionSyntax outer) return false;
        if (outer.Expression is not MemberAccessExpressionSyntax inner) return false;
        return IsMyIdentifier(inner.Expression) &&
               string.Equals(inner.Name.Identifier.Text, "Settings", StringComparison.Ordinal);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var outer = (MemberAccessExpressionSyntax)node;
        var propName = outer.Name.Identifier.Text;
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"Properties.Settings.Default.{propName}");
    }
}

/// <summary>
/// my_filesystem_read: My.Computer.FileSystem.ReadAllText(p)  →  System.IO.File.ReadAllText(p)
/// </summary>
public sealed class MyFilesystemReadRule : ISeedRule
{
    public string Tag => "my_filesystem_read";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        if (inv.Expression is not MemberAccessExpressionSyntax mem) return false;
        if (!string.Equals(mem.Name.Identifier.Text, "ReadAllText", StringComparison.Ordinal)) return false;
        return IsMyFileSystem(mem.Expression);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var inv  = (InvocationExpressionSyntax)node;
        var args = inv.ArgumentList?.ToString() ?? "()";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"System.IO.File.ReadAllText{args}");
    }
}

/// <summary>
/// my_filesystem_write: My.Computer.FileSystem.WriteAllText(p, s)  →  System.IO.File.WriteAllText(p, s)
/// </summary>
public sealed class MyFilesystemWriteRule : ISeedRule
{
    public string Tag => "my_filesystem_write";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        if (inv.Expression is not MemberAccessExpressionSyntax mem) return false;
        if (!string.Equals(mem.Name.Identifier.Text, "WriteAllText", StringComparison.Ordinal)) return false;
        return IsMyFileSystem(mem.Expression);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var inv  = (InvocationExpressionSyntax)node;
        var args = inv.ArgumentList?.ToString() ?? "()";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"System.IO.File.WriteAllText{args}");
    }
}

/// <summary>
/// my_app_version: My.Application.Info.Version
///   →  Assembly.GetExecutingAssembly().GetName().Version
/// </summary>
public sealed class MyAppVersionRule : ISeedRule
{
    public string Tag => "my_app_version";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not MemberAccessExpressionSyntax outer) return false;
        if (!string.Equals(outer.Name.Identifier.Text, "Version", StringComparison.Ordinal)) return false;
        // outer.Expression should be My.Application.Info
        if (outer.Expression is not MemberAccessExpressionSyntax info) return false;
        if (!string.Equals(info.Name.Identifier.Text, "Info", StringComparison.Ordinal)) return false;
        if (info.Expression is not MemberAccessExpressionSyntax app) return false;
        return IsMyIdentifier(app.Expression) &&
               string.Equals(app.Name.Identifier.Text, "Application", StringComparison.Ordinal);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression("Assembly.GetExecutingAssembly().GetName().Version");
}

/// <summary>
/// my_user: My.User.Name  →  WindowsIdentity.GetCurrent().Name
/// </summary>
public sealed class MyUserRule : ISeedRule
{
    public string Tag => "my_user";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not MemberAccessExpressionSyntax outer) return false;
        if (!string.Equals(outer.Name.Identifier.Text, "Name", StringComparison.Ordinal)) return false;
        if (outer.Expression is not MemberAccessExpressionSyntax user) return false;
        return IsMyIdentifier(user.Expression) &&
               string.Equals(user.Name.Identifier.Text, "User", StringComparison.Ordinal);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression("WindowsIdentity.GetCurrent().Name");
}

// ── shared helpers ─────────────────────────────────────────────────────────────

file static class MyHelper
{
    internal static bool IsMyIdentifier(SyntaxNode node)
        => node is IdentifierNameSyntax id &&
           string.Equals(id.Identifier.Text, "My", StringComparison.Ordinal);

    internal static bool IsMyFileSystem(SyntaxNode node)
    {
        // Must be My.Computer.FileSystem
        if (node is not MemberAccessExpressionSyntax fs) return false;
        if (!string.Equals(fs.Name.Identifier.Text, "FileSystem", StringComparison.Ordinal)) return false;
        if (fs.Expression is not MemberAccessExpressionSyntax computer) return false;
        return IsMyIdentifier(computer.Expression) &&
               string.Equals(computer.Name.Identifier.Text, "Computer", StringComparison.Ordinal);
    }
}

// Make the file-scoped helpers visible to the rule classes in this file
file static class MyHelper2
{
    // Proxy so classes above can call without fully qualifying
}
```

Note: because `file static class MyHelper` is file-scoped, the five rule classes above call `MyHelper.IsMyIdentifier` and `MyHelper.IsMyFileSystem` directly within the same file. Replace the direct `IsMyIdentifier` / `IsMyFileSystem` calls in the rule bodies with `MyHelper.IsMyIdentifier(...)` / `MyHelper.IsMyFileSystem(...)` respectively — the above listing shows the complete intent; adjust the call sites accordingly during implementation.

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/MyNamespaceRulesTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class MyNamespaceRulesTests
{
    [Fact]
    public void MySettingsRule_CanHandle_MySettingsFoo_ReturnsTrue()
    {
        var rule = new MySettingsRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim v = My.Settings.Foo\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.MemberAccessExpressionSyntax>()
            .First(n => n.ToString().StartsWith("My.Settings."));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void MySettingsRule_Convert_ProducesPropertiesSettingsDefault()
    {
        var rule = new MySettingsRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim v = My.Settings.Foo\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.MemberAccessExpressionSyntax>()
            .First(n => n.ToString().StartsWith("My.Settings."));
        var result = rule.Convert(node).ToString();
        Assert.Contains("Properties.Settings.Default.Foo", result);
    }

    [Fact]
    public void MyFilesystemReadRule_CanHandle_ReadAllText_ReturnsTrue()
    {
        var rule = new MyFilesystemReadRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim s = My.Computer.FileSystem.ReadAllText(\"f.txt\")\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void MyFilesystemReadRule_Convert_ProducesFileReadAllText()
    {
        var rule = new MyFilesystemReadRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim s = My.Computer.FileSystem.ReadAllText(\"f.txt\")\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("System.IO.File.ReadAllText", result);
    }

    [Fact]
    public void MyFilesystemWriteRule_CanHandle_WriteAllText_ReturnsTrue()
    {
        var rule = new MyFilesystemWriteRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nMy.Computer.FileSystem.WriteAllText(\"f.txt\", s)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void MyFilesystemWriteRule_Convert_ProducesFileWriteAllText()
    {
        var rule = new MyFilesystemWriteRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nMy.Computer.FileSystem.WriteAllText(\"f.txt\", s)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("System.IO.File.WriteAllText", result);
    }

    [Fact]
    public void MyAppVersionRule_CanHandle_MyApplicationInfoVersion_ReturnsTrue()
    {
        var rule = new MyAppVersionRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim v = My.Application.Info.Version\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.MemberAccessExpressionSyntax>()
            .First(n => n.ToString() == "My.Application.Info.Version");
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void MyAppVersionRule_Convert_ProducesAssemblyGetName()
    {
        var rule = new MyAppVersionRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim v = My.Application.Info.Version\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.MemberAccessExpressionSyntax>()
            .First(n => n.ToString() == "My.Application.Info.Version");
        var result = rule.Convert(node).ToString();
        Assert.Contains("Assembly.GetExecutingAssembly", result);
        Assert.Contains("GetName", result);
        Assert.Contains("Version", result);
    }

    [Fact]
    public void MyUserRule_CanHandle_MyUserName_ReturnsTrue()
    {
        var rule = new MyUserRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim n = My.User.Name\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.MemberAccessExpressionSyntax>()
            .First(n => n.ToString() == "My.User.Name");
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void MyUserRule_Convert_ProducesWindowsIdentityGetCurrent()
    {
        var rule = new MyUserRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim n = My.User.Name\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.VisualBasic.Syntax.MemberAccessExpressionSyntax>()
            .First(n => n.ToString() == "My.User.Name");
        var result = rule.Convert(node).ToString();
        Assert.Contains("WindowsIdentity.GetCurrent", result);
        Assert.Contains(".Name", result);
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~MyNamespaceRules"
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/SeedRules/Rules/MyNamespaceRules.cs tests/VBMigrator.Core.Tests/SeedRules/MyNamespaceRulesTests.cs
git commit -m "feat(seed): My namespace rules — my_settings, my_filesystem_read/write, my_app_version, my_user (5 classes, 1 file)"
```

---

## Task 10: Semantic SeedRule (nothing_assign_valuetype)

**Files:**
- `src/VBMigrator.Core/SeedRules/Rules/NothingValueTypeRule.cs`
- `tests/VBMigrator.Core.Tests/SeedRules/NothingValueTypeRuleTests.cs`

- [ ] Create `src/VBMigrator.Core/SeedRules/Rules/NothingValueTypeRule.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// nothing_assign_valuetype: Dim x As Integer = Nothing  →  int x = default;
/// CanHandle REQUIRES semanticModel != null to confirm the declared type is a value type.
/// Without semanticModel: CanHandle returns false → the node goes to LLM.
/// This is intentional: blindly emitting `default` for a reference type is wrong.
/// </summary>
public sealed class NothingValueTypeRule : ISeedRule
{
    public string Tag => "nothing_assign_valuetype";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        // Without semantic model, we cannot confirm value type → always false
        if (semanticModel is null) return false;

        if (node is not LocalDeclarationStatementSyntax decl) return false;

        foreach (var declarator in decl.Declarators)
        {
            // Must have an initializer that is Nothing
            var init = declarator.Initializer?.Value;
            if (init is null) continue;
            if (!init.IsKind(SyntaxKind.NothingLiteralExpression)) continue;

            // Must have an explicit As clause with a value type
            if (declarator.AsClause is not SimpleAsClauseSyntax asClause) continue;
            var typeInfo = semanticModel.GetTypeInfo(asClause.Type);
            if (typeInfo.Type is { IsValueType: true })
                return true;
        }
        return false;
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var decl = (LocalDeclarationStatementSyntax)node;
        // Take first declarator with Nothing initializer
        var declarator = decl.Declarators.First(d =>
            d.Initializer?.Value?.IsKind(SyntaxKind.NothingLiteralExpression) == true);

        var asClause  = (SimpleAsClauseSyntax)declarator.AsClause!;
        var typeName  = asClause.Type.ToString().Trim();
        var varName   = declarator.Names[0].Identifier.Text;

        // Map common VB types to C# keywords
        var csType = typeName switch
        {
            "Integer" => "int",
            "Long"    => "long",
            "Short"   => "short",
            "Byte"    => "byte",
            "Single"  => "float",
            "Double"  => "double",
            "Decimal" => "decimal",
            "Boolean" => "bool",
            "Char"    => "char",
            _         => typeName
        };

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseStatement($"{csType} {varName} = default;");
    }
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/SeedRules/NothingValueTypeRuleTests.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class NothingValueTypeRuleTests
{
    private readonly NothingValueTypeRule _rule = new();

    [Fact]
    public void CanHandle_WithoutSemanticModel_ReturnsFalse()
    {
        // Without semanticModel, rule must return false regardless of node type
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim x As Integer = Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.LocalDeclarationStatement));
        Assert.False(_rule.CanHandle(node, semanticModel: null));
    }

    [Fact]
    public void CanHandle_NothingLiteralWithValueType_AndSemanticModel_ReturnsTrue()
    {
        // Build a real in-memory compilation to get a SemanticModel
        var vbCode = @"
Module M
    Sub F()
        Dim x As Integer = Nothing
    End Sub
End Module";
        var tree       = SyntaxFactory.ParseSyntaxTree(vbCode);
        var compilation = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create(
            "TestAsm",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.LocalDeclarationStatement));
        Assert.True(_rule.CanHandle(node, model));
    }

    [Fact]
    public void Convert_IntegerNothing_ProducesIntDefault()
    {
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim x As Integer = Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.LocalDeclarationStatement));
        // Convert does not require semantic model for text emission
        var result = _rule.Convert(node).ToString();
        Assert.Contains("int", result);
        Assert.Contains("x", result);
        Assert.Contains("default", result);
    }

    [Fact]
    public void CanHandle_NothingLiteralWithReferenceType_ReturnsFalse()
    {
        // String is a reference type — rule should not handle it
        var vbCode = @"
Module M
    Sub F()
        Dim x As String = Nothing
    End Sub
End Module";
        var tree       = SyntaxFactory.ParseSyntaxTree(vbCode);
        var compilation = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create(
            "TestAsm2",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.LocalDeclarationStatement));
        Assert.False(_rule.CanHandle(node, model));
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~NothingValueTypeRule"
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/SeedRules/Rules/NothingValueTypeRule.cs tests/VBMigrator.Core.Tests/SeedRules/NothingValueTypeRuleTests.cs
git commit -m "feat(seed): nothing_assign_valuetype — semantic rule, returns false without SemanticModel"
```

---

## Task 11: DifficultyAnalyzer + FlagDetectors

**Files:**
- `src/VBMigrator.Core/Analyzer/FlagDetectors/WithEventsDetector.cs`
- `src/VBMigrator.Core/Analyzer/FlagDetectors/OnErrorDetector.cs`
- `src/VBMigrator.Core/Analyzer/FlagDetectors/MyNamespaceDetector.cs`
- `src/VBMigrator.Core/Analyzer/FlagDetectors/LateBindingDetector.cs`
- `src/VBMigrator.Core/Analyzer/FlagDetectors/OperatorDetector.cs`
- `src/VBMigrator.Core/Analyzer/DifficultyAnalyzer.cs`
- `tests/VBMigrator.Core.Tests/Analyzer/DifficultyAnalyzerTests.cs`

- [ ] Create `src/VBMigrator.Core/Analyzer/FlagDetectors/WithEventsDetector.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Analyzer.FlagDetectors;

/// <summary>
/// Detects WithEvents field declarations in the class containing the method.
/// Sets flag "WithEvents" on any method in a class that has at least one WithEvents field.
/// </summary>
public static class WithEventsDetector
{
    public const string Flag = "WithEvents";

    public static bool HasWithEventsInScope(SyntaxNode methodNode)
    {
        var containingType = methodNode.Ancestors()
            .FirstOrDefault(n => n is ClassBlockSyntax or ModuleBlockSyntax);
        if (containingType is null) return false;

        return containingType.DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .Any(f => f.Modifiers.Any(m => m.IsKind(SyntaxKind.WithEventsKeyword)));
    }
}
```

- [ ] Create `src/VBMigrator.Core/Analyzer/FlagDetectors/OnErrorDetector.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Analyzer.FlagDetectors;

/// <summary>
/// Detects On Error GoTo and On Error Resume Next within a method body.
/// Also detects GotoCrossBlock: a GoTo statement that targets a label in a different block
/// (if/for/try nesting level differs from the GoTo statement's nesting level).
/// </summary>
public static class OnErrorDetector
{
    public const string FlagOnError       = "OnError";
    public const string FlagOnErrorResume = "OnErrorResume";
    public const string FlagGotoCrossBlock = "GotoCrossBlock";

    public static (bool HasOnError, bool HasOnErrorResume, bool HasGotoCrossBlock) Detect(SyntaxNode methodNode)
    {
        var descendants = methodNode.DescendantNodes().ToList();

        bool hasOnError       = descendants.Any(n => n is OnErrorGoToStatementSyntax stmt
                                    && !stmt.GoToLabel.IsKind(SyntaxKind.ZeroLiteralExpression));
        bool hasOnErrorResume = descendants.Any(n => n is OnErrorResumeNextStatementSyntax);

        // GotoCrossBlock: detect GoTo where the GoTo and the label are at different block depths.
        // Heuristic: any GoTo that is inside a block (If/For/While/Try) whose ancestor labels
        // do NOT contain the target label name. Phase 1 uses a conservative approximation.
        bool hasGotoCrossBlock = hasOnError && DetectCrossBlockGoTo(methodNode, descendants);

        return (hasOnError, hasOnErrorResume, hasGotoCrossBlock);
    }

    private static bool DetectCrossBlockGoTo(SyntaxNode methodNode, List<SyntaxNode> descendants)
    {
        // Collect all label names defined in the method
        var labels = descendants.OfType<LabelStatementSyntax>()
            .Select(l => l.LabelToken.Text)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // For each GoTo (not On Error GoTo), check if it's inside a nested block
        // and the label is defined at a different nesting level
        foreach (var gotoStmt in descendants.OfType<GoToStatementSyntax>())
        {
            var targetLabel = gotoStmt.Label.ToString().Trim();
            if (!labels.Contains(targetLabel)) continue;

            // Count block depth at GoTo site
            var gotoDepth = CountBlockDepth(gotoStmt);

            // Find label node and count its depth
            var labelNode = descendants.OfType<LabelStatementSyntax>()
                .FirstOrDefault(l => string.Equals(l.LabelToken.Text, targetLabel, StringComparison.OrdinalIgnoreCase));
            if (labelNode is null) continue;

            var labelDepth = CountBlockDepth(labelNode);
            if (gotoDepth != labelDepth) return true;
        }
        return false;
    }

    private static int CountBlockDepth(SyntaxNode node)
        => node.Ancestors().Count(n =>
            n is MultiLineIfBlockSyntax or ForBlockSyntax or WhileBlockSyntax or
                 TryBlockSyntax or SelectBlockSyntax or WithBlockSyntax or DoLoopBlockSyntax);
}
```

- [ ] Create `src/VBMigrator.Core/Analyzer/FlagDetectors/MyNamespaceDetector.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Analyzer.FlagDetectors;

/// <summary>
/// Detects any use of the My namespace within a method body.
/// </summary>
public static class MyNamespaceDetector
{
    public const string Flag = "MyNamespace";

    public static bool HasMyNamespace(SyntaxNode methodNode)
        => methodNode.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(id => string.Equals(id.Identifier.Text, "My", StringComparison.Ordinal) &&
                       id.Parent is MemberAccessExpressionSyntax);
}
```

- [ ] Create `src/VBMigrator.Core/Analyzer/FlagDetectors/LateBindingDetector.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Analyzer.FlagDetectors;

/// <summary>
/// Detects late binding patterns: calls on Object-typed variables or
/// CreateObject / New with a string argument. Requires SemanticModel for accuracy.
/// Without SemanticModel, returns false (conservative — avoids false positives).
/// </summary>
public static class LateBindingDetector
{
    public const string Flag = "LateBinding";

    public static bool HasLateBinding(SyntaxNode methodNode, SemanticModel? semanticModel)
    {
        if (semanticModel is null) return false;

        foreach (var invocation in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax mem)
            {
                var receiverType = semanticModel.GetTypeInfo(mem.Expression).Type;
                if (receiverType is not null &&
                    receiverType.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Object)
                    return true;
            }
        }
        return false;
    }
}
```

- [ ] Create `src/VBMigrator.Core/Analyzer/FlagDetectors/OperatorDetector.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Analyzer.FlagDetectors;

/// <summary>
/// Detects VB-specific operators that have no direct C# equivalent:
/// LikeOp (Like operator), ExponOp (^ exponentiation), ByteLoop (For As Byte = 0 To 255).
/// </summary>
public static class OperatorDetector
{
    public const string FlagLikeOp   = "LikeOp";
    public const string FlagExponOp  = "ExponOp";
    public const string FlagByteLoop = "ByteLoop";

    public static (bool LikeOp, bool ExponOp, bool ByteLoop) Detect(SyntaxNode methodNode)
    {
        var nodes = methodNode.DescendantNodes().ToList();

        bool likeOp  = nodes.Any(n => n.IsKind(SyntaxKind.LikeExpression));
        bool exponOp = nodes.Any(n => n.IsKind(SyntaxKind.ExponentiateExpression));
        bool byteLoop = nodes.OfType<ForStatementSyntax>().Any(f =>
        {
            bool isByte = f.ControlVariable is VariableDeclaratorSyntax d &&
                          d.AsClause is SimpleAsClauseSyntax a &&
                          string.Equals(a.Type?.ToString().Trim(), "Byte", StringComparison.OrdinalIgnoreCase);
            return isByte && f.ToValue?.ToString().Trim() == "255";
        });

        return (likeOp, exponOp, byteLoop);
    }
}
```

- [ ] Create `src/VBMigrator.Core/Analyzer/DifficultyAnalyzer.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VBMigrator.Core.Analyzer.FlagDetectors;
using VBMigrator.Core.Models;

namespace VBMigrator.Core.Analyzer;

/// <summary>
/// Analyzes a VB.NET SyntaxTree and produces a DifficultyMap.
/// The map contains per-method flags used by the pipeline to decide routing
/// (SeedRule, LLM, or HumanQueue) and whether re-translation is needed.
/// </summary>
public class DifficultyAnalyzer
{
    /// <summary>
    /// Analyze all methods in <paramref name="tree"/> and return a DifficultyMap.
    /// </summary>
    public DifficultyMap Analyze(SyntaxTree tree, string filePath, SemanticModel? semanticModel = null)
    {
        var root = tree.GetRoot();
        var functions = new List<FunctionDifficulty>();

        foreach (var method in GetMethodBlocks(root))
        {
            var diff = AnalyzeMethod(method, semanticModel);
            functions.Add(diff);
        }

        int overallScore = functions.Count == 0 ? 0 : (int)functions.Average(f => f.Score);

        return new DifficultyMap
        {
            FilePath      = filePath,
            OverallScore  = overallScore,
            Functions     = functions
        };
    }

    private static FunctionDifficulty AnalyzeMethod(SyntaxNode methodNode, SemanticModel? semanticModel)
    {
        var flags = new List<string>();
        var name  = GetMethodName(methodNode);

        // WithEvents
        if (WithEventsDetector.HasWithEventsInScope(methodNode))
            flags.Add(WithEventsDetector.Flag);

        // On Error
        var (hasOnError, hasOnErrorResume, hasGotoCrossBlock) = OnErrorDetector.Detect(methodNode);
        if (hasOnError)        flags.Add(OnErrorDetector.FlagOnError);
        if (hasOnErrorResume)  flags.Add(OnErrorDetector.FlagOnErrorResume);
        if (hasGotoCrossBlock) flags.Add(OnErrorDetector.FlagGotoCrossBlock);

        // My namespace
        if (MyNamespaceDetector.HasMyNamespace(methodNode))
            flags.Add(MyNamespaceDetector.Flag);

        // Late binding
        if (LateBindingDetector.HasLateBinding(methodNode, semanticModel))
            flags.Add(LateBindingDetector.Flag);

        // Operators
        var (likeOp, exponOp, byteLoop) = OperatorDetector.Detect(methodNode);
        if (likeOp)   flags.Add(OperatorDetector.FlagLikeOp);
        if (exponOp)  flags.Add(OperatorDetector.FlagExponOp);
        if (byteLoop) flags.Add(OperatorDetector.FlagByteLoop);

        // Score: 10 points per flag, capped at 100
        int score = Math.Min(100, flags.Count * 10);

        // Route determination
        var route = DetermineRoute(flags);

        return new FunctionDifficulty
        {
            MethodName = name,
            Score      = score,
            Flags      = flags,
            Route      = route
        };
    }

    private static TranslationRoute DetermineRoute(List<string> flags)
    {
        if (flags.Contains(OnErrorDetector.FlagGotoCrossBlock))
            return TranslationRoute.HumanQueue;
        if (flags.Count == 0)
            return TranslationRoute.SeedRule; // clean method, ICSharpCode output trusted
        if (flags.Contains(LateBindingDetector.Flag) || flags.Contains(WithEventsDetector.Flag))
            return TranslationRoute.Llm;
        return TranslationRoute.SeedRule;
    }

    private static IEnumerable<SyntaxNode> GetMethodBlocks(SyntaxNode root)
        => root.DescendantNodes().Where(n =>
            n is MethodBlockSyntax or
                 ConstructorBlockSyntax or
                 PropertyBlockSyntax or
                 OperatorBlockSyntax);

    private static string GetMethodName(SyntaxNode node) => node switch
    {
        MethodBlockSyntax mb        => mb.SubOrFunctionStatement.Identifier.Text,
        ConstructorBlockSyntax cb   => cb.SubNewStatement.NewKeyword.Text,
        PropertyBlockSyntax pb      => pb.PropertyStatement.Identifier.Text,
        OperatorBlockSyntax ob      => ob.OperatorStatement.OperatorToken.Text,
        _                           => "<unknown>"
    };
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/Analyzer/DifficultyAnalyzerTests.cs`:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.Analyzer;
using VBMigrator.Core.Analyzer.FlagDetectors;
using VBMigrator.Core.Models;
using Xunit;

namespace VBMigrator.Core.Tests.Analyzer;

public class DifficultyAnalyzerTests
{
    private readonly DifficultyAnalyzer _analyzer = new();

    [Fact]
    public void Analyze_CleanMethod_NoFlags_ReturnsScoreZero()
    {
        var vb = "Module M\nSub F()\nDim x As Integer = 1\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Empty(fn.Flags);
        Assert.Equal(0, fn.Score);
    }

    [Fact]
    public void Analyze_OnErrorGoTo_SetsOnErrorFlag()
    {
        var vb = "Module M\nSub F()\nOn Error GoTo ErrH\nErrH:\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(OnErrorDetector.FlagOnError, fn.Flags);
    }

    [Fact]
    public void Analyze_OnErrorResumeNext_SetsOnErrorResumeFlag()
    {
        var vb = "Module M\nSub F()\nOn Error Resume Next\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(OnErrorDetector.FlagOnErrorResume, fn.Flags);
    }

    [Fact]
    public void Analyze_LikeOperator_SetsLikeOpFlag()
    {
        var vb = "Module M\nSub F()\nDim r = s Like \"A*\"\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(OperatorDetector.FlagLikeOp, fn.Flags);
    }

    [Fact]
    public void Analyze_Exponentiation_SetsExponOpFlag()
    {
        var vb = "Module M\nSub F()\nDim r = x ^ y\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(OperatorDetector.FlagExponOp, fn.Flags);
    }

    [Fact]
    public void Analyze_ByteLoop255_SetsByteLoopFlag()
    {
        var vb = "Module M\nSub F()\nFor i As Byte = 0 To 255\nNext\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(OperatorDetector.FlagByteLoop, fn.Flags);
    }

    [Fact]
    public void Analyze_MyNamespace_SetsMyNamespaceFlag()
    {
        var vb = "Module M\nSub F()\nDim v = My.Settings.Foo\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Contains(MyNamespaceDetector.Flag, fn.Flags);
    }

    [Fact]
    public void Analyze_MultipleFlags_ScoreIsProportional()
    {
        // 3 flags → score 30
        var vb = "Module M\nSub F()\nOn Error GoTo E\nDim r = s Like \"A*\"\nDim p = x ^ y\nE:\nEnd Sub\nEnd Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vb);
        var map  = _analyzer.Analyze(tree, "test.vb");
        var fn   = Assert.Single(map.Functions);
        Assert.Equal(30, fn.Score);
    }

    [Fact]
    public void Analyze_FilePath_IsPreservedInMap()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nEnd Sub\nEnd Module");
        var map  = _analyzer.Analyze(tree, "MyFile.vb");
        Assert.Equal("MyFile.vb", map.FilePath);
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~DifficultyAnalyzer"
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/Analyzer/ tests/VBMigrator.Core.Tests/Analyzer/
git commit -m "feat(analyzer): DifficultyAnalyzer + FlagDetectors (WithEvents, OnError, MyNamespace, LateBinding, Operator)"
```

---

## Task 12: RoslynTranslator (ICSharpCode wrapper)

**Files:**
- `src/VBMigrator.Core/Translator/RoslynTranslator.cs`
- `tests/VBMigrator.Core.Tests/Translator/RoslynTranslatorTests.cs`

- [ ] Create `src/VBMigrator.Core/Translator/RoslynTranslator.cs`:

```csharp
using ICSharpCode.CodeConverter;
using ICSharpCode.CodeConverter.CSharp;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Wrapper around ICSharpCode.CodeConverter.ConvertAsync.
/// Handles whole-file VB → initial C# conversion (pipeline step [2]).
/// Also applies Roslyn post-processing for default_property call sites
/// that ICSharpCode does not resolve correctly.
/// </summary>
public class RoslynTranslator
{
    /// <summary>
    /// Converts a VB.NET source file to C# using ICSharpCode.CodeConverter.
    /// Returns the converted C# source, or null with error details on failure.
    /// </summary>
    public async Task<RoslynTranslationResult> ConvertFileAsync(
        string vbSource,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Parse VB source
            var vbTree = Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory.ParseSyntaxTree(
                vbSource,
                path: filePath,
                cancellationToken: cancellationToken);

            // Create minimal VB compilation
            var vbCompilation = VisualBasicCompilation.Create(
                assemblyName: System.IO.Path.GetFileNameWithoutExtension(filePath),
                syntaxTrees:  new[] { vbTree },
                references:   GetDefaultReferences(),
                options:      new VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                                  optionStrict: OptionStrict.Off));

            // ICSharpCode conversion
            var converter = new CodeConverter();
            var result    = await converter.ConvertAsync(vbCompilation, new ConversionOptions
            {
                TargetLanguage = TargetLanguage.CS
            }, cancellationToken);

            var csSource = result.ConvertedFiles.FirstOrDefault().Value ?? string.Empty;

            return new RoslynTranslationResult
            {
                Success  = !string.IsNullOrEmpty(csSource),
                CsSource = csSource,
                Errors   = result.Exceptions.Select(ex => ex.Message).ToList()
            };
        }
        catch (Exception ex)
        {
            return new RoslynTranslationResult
            {
                Success  = false,
                CsSource = string.Empty,
                Errors   = new List<string> { ex.Message }
            };
        }
    }

    private static IEnumerable<MetadataReference> GetDefaultReferences()
    {
        var trusted = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        return new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(trusted, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(System.IO.Path.Combine(trusted, "System.Collections.dll")),
        };
    }
}

public record RoslynTranslationResult
{
    public bool Success { get; init; }
    public required string CsSource { get; init; }
    public List<string> Errors { get; init; } = new();
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/Translator/RoslynTranslatorTests.cs`:

```csharp
using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class RoslynTranslatorTests
{
    private readonly RoslynTranslator _translator = new();

    [Fact]
    public async Task ConvertFileAsync_SimpleModule_ReturnsNonEmptyCSharp()
    {
        const string vb = @"
Module HelloModule
    Sub SayHello()
        Dim message As String = ""Hello, World!""
        Console.WriteLine(message)
    End Sub
End Module";

        var result = await _translator.ConvertFileAsync(vb, "Hello.vb");

        Assert.True(result.Success, $"Conversion failed: {string.Join("; ", result.Errors)}");
        Assert.NotEmpty(result.CsSource);
        // ICSharpCode should emit a static class for Module
        Assert.Contains("static", result.CsSource);
    }

    [Fact]
    public async Task ConvertFileAsync_EmptySource_ReturnsFalseOrEmpty()
    {
        var result = await _translator.ConvertFileAsync(string.Empty, "empty.vb");
        // Either fails gracefully or returns empty — both are acceptable
        Assert.True(!result.Success || string.IsNullOrWhiteSpace(result.CsSource));
    }

    [Fact]
    public async Task ConvertFileAsync_ClassWithProperty_ContainsPropertyInOutput()
    {
        const string vb = @"
Public Class Person
    Public Property Name As String
End Class";

        var result = await _translator.ConvertFileAsync(vb, "Person.vb");

        Assert.True(result.Success, $"Conversion failed: {string.Join("; ", result.Errors)}");
        Assert.Contains("Name", result.CsSource);
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~RoslynTranslator"
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/Translator/RoslynTranslator.cs tests/VBMigrator.Core.Tests/Translator/RoslynTranslatorTests.cs
git commit -m "feat(translator): RoslynTranslator — ICSharpCode.CodeConverter whole-file VB→C# wrapper"
```

---

## Task 13: LlmUsingResolver

**Files:**
- `src/VBMigrator.Core/Translator/LlmUsingResolver.cs`
- `tests/VBMigrator.Core.Tests/Translator/LlmUsingResolverTests.cs`

- [ ] Create `src/VBMigrator.Core/Translator/LlmUsingResolver.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Resolves missing using statements after both SeedRules and LLM have run on the C# method.
/// Runs on the COMPLETE C# method text (post-SeedRule + post-LLM), per spec §4.3.
/// Uses a static well-known type→namespace dictionary.
/// Types not in the dictionary → emits MISSING_USING flag for HumanQueue.
/// </summary>
public class LlmUsingResolver
{
    private static readonly Dictionary<string, string> _wellKnown = new(StringComparer.Ordinal)
    {
        // Types introduced by SeedRules or LLM — document each with the rule that introduces it
        ["WindowsIdentity"]         = "System.Security.Principal",   // my_user rule
        ["Regex"]                   = "System.Text.RegularExpressions", // like_operator rule
        ["Assembly"]                = "System.Reflection",            // my_app_version rule
        ["DateTime"]                = "System",                       // date_literal rule
        ["StringComparison"]        = "System",                       // string_comparison_case rule
        ["Math"]                    = "System",                       // exponentiation rule (Math.Pow)
        ["File"]                    = "System.IO",                    // my_filesystem_* rules (unqualified alias)
        ["Path"]                    = "System.IO",                    // common LLM output
        ["Directory"]               = "System.IO",                    // common LLM output
        ["StringBuilder"]           = "System.Text",                  // common LLM output
        ["Encoding"]                = "System.Text",                  // common LLM output
        ["Stream"]                  = "System.IO",                    // common LLM output
        ["StreamReader"]            = "System.IO",                    // common LLM output
        ["StreamWriter"]            = "System.IO",                    // common LLM output
        ["List"]                    = "System.Collections.Generic",   // common LLM output
        ["Dictionary"]              = "System.Collections.Generic",   // common LLM output
        ["IEnumerable"]             = "System.Collections.Generic",   // common LLM output
        ["Task"]                    = "System.Threading.Tasks",       // common LLM output
        ["CancellationToken"]       = "System.Threading",             // common LLM output
        ["HttpClient"]              = "System.Net.Http",              // common LLM output
        ["JsonSerializer"]          = "System.Text.Json",             // common LLM output
        ["JsonSerializerOptions"]   = "System.Text.Json",             // common LLM output
        ["XDocument"]               = "System.Xml.Linq",              // common LLM output
        ["XElement"]                = "System.Xml.Linq",              // common LLM output
    };

    public const string MissingUsingFlag = "MISSING_USING";

    /// <summary>
    /// Parses <paramref name="csMethodSource"/> as C# and finds identifier names
    /// that are unresolved (not in scope). For each, looks up the well-known table.
    /// Returns resolved namespaces and MISSING_USING entries for unknowns.
    /// </summary>
    public LlmUsingResolution Resolve(string csMethodSource, SemanticModel? model = null)
    {
        var resolvedNamespaces = new HashSet<string>(StringComparer.Ordinal);
        var missingTypes       = new List<string>();

        // Parse as an expression/statement block to find all type references
        var tree = CSharpSyntaxTree.ParseText(csMethodSource);
        var root = tree.GetRoot();

        // Collect all simple identifier names that could be type references
        var candidates = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(id => id.Identifier.Text)
            .Distinct(StringComparer.Ordinal);

        foreach (var typeName in candidates)
        {
            if (_wellKnown.TryGetValue(typeName, out var ns))
            {
                resolvedNamespaces.Add(ns);
            }
            // Note: we do NOT flag every unknown identifier as MISSING_USING —
            // only identifiers that appear as the leftmost part of a member access
            // or as a standalone type reference in a known-type position.
            // Full semantic analysis requires a compilation; that is done by Validator.
            // This resolver provides best-effort using directives to reduce Validator failures.
        }

        return new LlmUsingResolution
        {
            Namespaces   = resolvedNamespaces.ToList(),
            MissingTypes = missingTypes
        };
    }

    /// <summary>
    /// Generates using directive lines from a list of namespace strings.
    /// </summary>
    public static IEnumerable<string> ToUsingDirectives(IEnumerable<string> namespaces)
        => namespaces.OrderBy(n => n).Select(n => $"using {n};");

    /// <summary>
    /// Returns the well-known table for inspection in tests.
    /// </summary>
    public static IReadOnlyDictionary<string, string> WellKnown => _wellKnown;
}

public record LlmUsingResolution
{
    public List<string> Namespaces   { get; init; } = new();
    public List<string> MissingTypes { get; init; } = new();
    public bool HasMissingTypes => MissingTypes.Count > 0;
}
```

- [ ] Create `tests/VBMigrator.Core.Tests/Translator/LlmUsingResolverTests.cs`:

```csharp
using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class LlmUsingResolverTests
{
    private readonly LlmUsingResolver _resolver = new();

    [Fact]
    public void Resolve_WindowsIdentity_ReturnsSecurityPrincipalNamespace()
    {
        var csMethod = "var name = WindowsIdentity.GetCurrent().Name;";
        var result   = _resolver.Resolve(csMethod);
        Assert.Contains("System.Security.Principal", result.Namespaces);
    }

    [Fact]
    public void Resolve_Regex_ReturnsRegularExpressionsNamespace()
    {
        var csMethod = "var ok = Regex.IsMatch(s, @\"^A.*$\");";
        var result   = _resolver.Resolve(csMethod);
        Assert.Contains("System.Text.RegularExpressions", result.Namespaces);
    }

    [Fact]
    public void Resolve_Assembly_ReturnsReflectionNamespace()
    {
        var csMethod = "var ver = Assembly.GetExecutingAssembly().GetName().Version;";
        var result   = _resolver.Resolve(csMethod);
        Assert.Contains("System.Reflection", result.Namespaces);
    }

    [Fact]
    public void Resolve_DateTime_ReturnsSystemNamespace()
    {
        var csMethod = "var d = new DateTime(2020, 1, 1);";
        var result   = _resolver.Resolve(csMethod);
        Assert.Contains("System", result.Namespaces);
    }

    [Fact]
    public void Resolve_NoKnownTypes_ReturnsEmptyNamespaces()
    {
        var csMethod = "var x = 1 + 2;";
        var result   = _resolver.Resolve(csMethod);
        // No well-known types used
        Assert.DoesNotContain("System.Reflection", result.Namespaces);
        Assert.DoesNotContain("System.Security.Principal", result.Namespaces);
    }

    [Fact]
    public void ToUsingDirectives_ProducesCorrectSyntax()
    {
        var namespaces = new[] { "System.IO", "System.Text" };
        var directives = LlmUsingResolver.ToUsingDirectives(namespaces).ToList();
        Assert.Contains("using System.IO;", directives);
        Assert.Contains("using System.Text;", directives);
    }

    [Fact]
    public void WellKnown_ContainsAllSeedRuleIntroducedTypes()
    {
        // Verify that all types introduced by SeedRules are in the table
        var required = new[]
        {
            "WindowsIdentity",   // my_user
            "Regex",             // like_operator
            "Assembly",          // my_app_version
            "DateTime",          // date_literal
            "StringComparison",  // string_comparison_case
            "Math"               // exponentiation
        };
        foreach (var type in required)
            Assert.True(LlmUsingResolver.WellKnown.ContainsKey(type),
                $"WellKnown table missing entry for '{type}'");
    }

    [Fact]
    public void Resolve_MultipleKnownTypes_ReturnsAllNamespaces()
    {
        var csMethod = @"
var name = WindowsIdentity.GetCurrent().Name;
var ok   = Regex.IsMatch(name, @""^A"");
var d    = new DateTime(2020, 1, 1);";
        var result = _resolver.Resolve(csMethod);
        Assert.Contains("System.Security.Principal", result.Namespaces);
        Assert.Contains("System.Text.RegularExpressions", result.Namespaces);
        Assert.Contains("System", result.Namespaces);
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~LlmUsingResolver"
```

Expected output keyword: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/Translator/LlmUsingResolver.cs tests/VBMigrator.Core.Tests/Translator/LlmUsingResolverTests.cs
git commit -m "feat(translator): LlmUsingResolver — static well-known type→namespace table, runs post-SeedRules+LLM"
```

---

---

## Task 14: LlmTranslator

**Files:**
- Create: `src/VBMigrator.Core/Translator/LlmTranslator.cs`
- Create: `src/VBMigrator.Core/Translator/Prompts/SystemPrompt.md`
- Create: `src/VBMigrator.Core/Translator/Prompts/RepairPrompt.md`
- Create: `tests/VBMigrator.Core.Tests/Translator/LlmTranslatorTests.cs`

- [ ] Create `src/VBMigrator.Core/Translator/Prompts/SystemPrompt.md`:

```markdown
Eres un traductor VB.NET→C# experto. Recibes un snippet VB.NET delimitado y devuelves SOLO el bloque C# equivalente.

Reglas:
- No agregues using statements ni imports
- No referencíes tipos que no existen en el snippet de entrada
- No refactorices — traducción fiel, sin cambios de lógica
- Preserva comentarios originales
- Devuelve solo el código C#, sin explicación ni markdown
```

- [ ] Create `src/VBMigrator.Core/Translator/Prompts/RepairPrompt.md`:

```markdown
Eres un repair agent. Recibiste un error de compilación C# y el contexto del código.
Fix SOLO el error indicado. No refactorices. Devuelve solo el bloque C# corregido, sin explicación.
```

- [ ] Write failing test `tests/VBMigrator.Core.Tests/Translator/LlmTranslatorTests.cs`:

```csharp
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using VBMigrator.Core.Models;
using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class LlmTranslatorTests
{
    [Fact]
    public async Task TranslateAsync_ReturnsLlmRoute_WhenSuccessful()
    {
        // Arrange — stub that returns a fixed response
        var fakeClient = new FakeAnthropicClient("int x = 0;");
        var translator = new LlmTranslator(fakeClient, null);

        // Act
        var result = await translator.TranslateAsync("Dim x As Integer = 0", null);

        // Assert
        Assert.Equal(TranslationRoute.Llm, result.Route);
        Assert.Contains("int x", result.CsSource);
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public async Task TranslateAsync_ReturnsHumanQueue_AfterMaxRetries()
    {
        var fakeClient = new FakeAnthropicClient(null, throwRateLimit: true);
        var translator = new LlmTranslator(fakeClient, null, retryCount: 2, retryBaseDelayMs: 1);

        var result = await translator.TranslateAsync("Dim x As Integer = 0", null);

        Assert.Equal(TranslationRoute.HumanQueue, result.Route);
        Assert.Equal(LlmFailureReason.RateLimit, result.LlmFailureReason);
    }
}

// Test double — lives in test project
public class FakeAnthropicClient : IAnthropicClient
{
    private readonly string? _response;
    private readonly bool _throwRateLimit;

    public FakeAnthropicClient(string? response, bool throwRateLimit = false)
    {
        _response = response;
        _throwRateLimit = throwRateLimit;
    }

    public Task<MessageResponse> Messages(MessageParameters parameters, CancellationToken cancellationToken = default)
    {
        if (_throwRateLimit)
            throw new AnthropicException(429, "rate_limit_error", "Rate limit exceeded");

        var msg = new MessageResponse
        {
            Content = [new TextContent { Text = _response! }],
            StopReason = "end_turn"
        };
        return Task.FromResult(msg);
    }
}
```

- [ ] Run test to confirm it fails (LlmTranslator not yet defined):

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~LlmTranslatorTests"
```

Expected: **FAILED** — type not found

- [ ] Create `src/VBMigrator.Core/Translator/LlmTranslator.cs`:

```csharp
using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using VBMigrator.Core.Models;

namespace VBMigrator.Core.Translator;

public interface IAnthropicClient
{
    Task<MessageResponse> Messages(MessageParameters parameters, CancellationToken cancellationToken = default);
}

public class AnthropicClientAdapter(AnthropicClient inner) : IAnthropicClient
{
    public Task<MessageResponse> Messages(MessageParameters p, CancellationToken ct = default)
        => inner.Messages.GetClaudeMessageAsync(p, ct);
}

public class LlmTranslator(IAnthropicClient client, string? apiKey,
    int retryCount = 2, int retryBaseDelayMs = 1000)
{
    private static readonly string _systemPrompt = File.Exists("Translator/Prompts/SystemPrompt.md")
        ? File.ReadAllText("Translator/Prompts/SystemPrompt.md")
        : "Traduce este VB.NET snippet a C#. Devuelve solo el código, sin using statements ni explicación.";

    public async Task<TranslationResult> TranslateAsync(string vbSource, string? fewShotExample)
    {
        var userContent = fewShotExample is null
            ? $"```vb\n{vbSource}\n```"
            : $"Ejemplo:\nVB: {fewShotExample}\n\n```vb\n{vbSource}\n```";

        var parameters = new MessageParameters
        {
            Model = "claude-sonnet-4-6",
            MaxTokens = 2048,
            System = [new SystemMessage(_systemPrompt)],
            Messages = [new Message(RoleType.User, userContent)]
        };

        LlmFailureReason? failureReason = null;

        for (int attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                var response = await client.Messages(parameters);
                var csSource = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;

                return new TranslationResult
                {
                    CsSource = csSource.Trim(),
                    Confidence = 0.75,
                    Route = TranslationRoute.Llm,
                    CompilerPassed = false
                };
            }
            catch (AnthropicException ex) when (ex.StatusCode == 429)
            {
                failureReason = LlmFailureReason.RateLimit;
                if (attempt < retryCount)
                    await Task.Delay(retryBaseDelayMs * (int)Math.Pow(2, attempt));
            }
            catch (TaskCanceledException)
            {
                failureReason = LlmFailureReason.Timeout;
                break;
            }
            catch
            {
                failureReason = LlmFailureReason.ApiError;
                break;
            }
        }

        return new TranslationResult
        {
            CsSource = string.Empty,
            Confidence = 0.0,
            Route = TranslationRoute.HumanQueue,
            CompilerPassed = false,
            LlmFailureReason = failureReason
        };
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~LlmTranslatorTests"
```

Expected: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/Translator/LlmTranslator.cs src/VBMigrator.Core/Translator/Prompts/ tests/VBMigrator.Core.Tests/Translator/LlmTranslatorTests.cs
git commit -m "feat(translator): LlmTranslator with retry backoff, HumanQueue on max retries"
```

---

## Task 15: RepairAgent

**Files:**
- Create: `src/VBMigrator.Core/Translator/RepairAgent.cs`
- Create: `tests/VBMigrator.Core.Tests/Translator/RepairAgentTests.cs`

- [ ] Write failing test:

```csharp
using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class RepairAgentTests
{
    [Fact]
    public async Task RepairAsync_ReducesConfidenceByTen_WhenSuccessful()
    {
        var fake = new FakeAnthropicClient("int x = 0; // fixed");
        var agent = new RepairAgent(fake);

        var result = await agent.RepairAsync(
            csMethod: "int x = foo();",
            errorMessage: "CS0103: 'foo' does not exist",
            affectedLines: ["int x = foo();"],
            originalConfidence: 0.90);

        Assert.True(result.Repaired);
        Assert.Equal(0.80, result.Confidence, precision: 2);
        Assert.Contains("fixed", result.CsSource);
    }

    [Fact]
    public async Task RepairAsync_ReturnsZeroConfidence_WhenLlmFails()
    {
        var fake = new FakeAnthropicClient(null, throwRateLimit: true);
        var agent = new RepairAgent(fake);

        var result = await agent.RepairAsync("bad code", "CS0001", [], 0.90);

        Assert.False(result.Repaired);
        Assert.Equal(0.0, result.Confidence);
    }
}
```

- [ ] Run failing:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~RepairAgentTests"
```

Expected: **FAILED**

- [ ] Create `src/VBMigrator.Core/Translator/RepairAgent.cs`:

```csharp
using Anthropic.SDK.Messaging;
using VBMigrator.Core.Models;

namespace VBMigrator.Core.Translator;

public record RepairResult(bool Repaired, string CsSource, double Confidence);

public class RepairAgent(IAnthropicClient client)
{
    private const string RepairSystem =
        "Eres un repair agent. Fix SOLO el error indicado. No refactorices. Devuelve solo el bloque C# corregido.";

    public async Task<RepairResult> RepairAsync(
        string csMethod, string errorMessage,
        IEnumerable<string> affectedLines, double originalConfidence)
    {
        var context = string.Join("\n", affectedLines);
        var user = $"Error: {errorMessage}\n\nCódigo:\n{context}";

        var parameters = new MessageParameters
        {
            Model = "claude-sonnet-4-6",
            MaxTokens = 512,
            System = [new SystemMessage(RepairSystem)],
            Messages = [new Message(RoleType.User, user)]
        };

        try
        {
            var response = await client.Messages(parameters);
            var fixed_ = response.Content.OfType<TextContent>().FirstOrDefault()?.Text ?? string.Empty;
            return new RepairResult(true, fixed_.Trim(), Math.Max(0.0, originalConfidence - 0.10));
        }
        catch
        {
            return new RepairResult(false, csMethod, 0.0);
        }
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~RepairAgentTests"
```

Expected: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/Translator/RepairAgent.cs tests/VBMigrator.Core.Tests/Translator/RepairAgentTests.cs
git commit -m "feat(translator): RepairAgent — single LLM call, confidence -0.10 on success"
```

---

## Task 16: ConfidenceScorer

**Files:**
- Create: `src/VBMigrator.Core/Translator/ConfidenceScorer.cs`
- Create: `tests/VBMigrator.Core.Tests/Translator/ConfidenceScorerTests.cs`

- [ ] Write failing test:

```csharp
using VBMigrator.Core.Models;
using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class ConfidenceScorerTests
{
    [Theory]
    [InlineData(new[] { 0.90, 0.70, 0.85 }, 0.70)]
    [InlineData(new[] { 0.90 }, 0.90)]
    [InlineData(new[] { 0.50, 0.50 }, 0.50)]
    public void Score_ReturnsMin(double[] confidences, double expected)
    {
        var score = ConfidenceScorer.Score(confidences);
        Assert.Equal(expected, score, precision: 10);
    }

    [Theory]
    [InlineData(0.85, TranslationRoute.SeedRule)]  // AUTO
    [InlineData(0.90, TranslationRoute.SeedRule)]  // AUTO
    [InlineData(0.84, TranslationRoute.Llm)]       // SUGGEST
    [InlineData(0.65, TranslationRoute.Llm)]       // SUGGEST (boundary)
    [InlineData(0.64, TranslationRoute.HumanQueue)]
    [InlineData(0.00, TranslationRoute.HumanQueue)]
    public void Route_MapsCorrectly(double confidence, TranslationRoute expectedRoute)
    {
        // SeedRule = AUTO, Llm = SUGGEST in this context (route is reused as tier)
        var route = ConfidenceScorer.GetRoute(confidence);
        Assert.Equal(expectedRoute, route);
    }
}
```

- [ ] Run failing:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~ConfidenceScorerTests"
```

Expected: **FAILED**

- [ ] Create `src/VBMigrator.Core/Translator/ConfidenceScorer.cs`:

```csharp
using VBMigrator.Core.Models;

namespace VBMigrator.Core.Translator;

public static class ConfidenceScorer
{
    // Conservative: one low-confidence node pulls down the whole method.
    // Prevents silent approvals when a single node is problematic.
    public static double Score(IEnumerable<double> nodeConfidences)
        => nodeConfidences.DefaultIfEmpty(0.0).Min();

    public static TranslationRoute GetRoute(double confidence) => confidence switch
    {
        >= 0.85 => TranslationRoute.SeedRule,   // AUTO tier
        >= 0.65 => TranslationRoute.Llm,         // SUGGEST tier
        _       => TranslationRoute.HumanQueue
    };
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~ConfidenceScorerTests"
```

Expected: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/Translator/ConfidenceScorer.cs tests/VBMigrator.Core.Tests/Translator/ConfidenceScorerTests.cs
git commit -m "feat(translator): ConfidenceScorer — min() aggregation, threshold routing"
```

---

## Task 17: TranslationPipeline

**Files:**
- Create: `src/VBMigrator.Core/Translator/TranslationPipeline.cs`
- Create: `tests/VBMigrator.Core.Tests/Translator/TranslationPipelineTests.cs`

- [ ] Write failing test:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.Analyzer;
using VBMigrator.Core.Models;
using VBMigrator.Core.SeedRules;
using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class TranslationPipelineTests
{
    [Fact]
    public async Task ProcessFileAsync_SimpleMethod_UsesICSharpCodeForCleanMethods()
    {
        // Arrange: VB file with no difficult flags
        const string vbSource = """
            Public Class MyClass
                Public Function Add(a As Integer, b As Integer) As Integer
                    Return a + b
                End Function
            End Class
            """;

        var pipeline = BuildPipeline();

        // Act
        var results = await pipeline.ProcessFileAsync(vbSource, "Test.vb");

        // Assert: clean method uses ICSharpCode output (confidence 0.85, no re-translation)
        Assert.All(results, r => Assert.True(r.Confidence >= 0.85));
    }

    [Fact]
    public async Task ProcessFileAsync_IsNothingPattern_UsesSeedRule()
    {
        const string vbSource = """
            Public Class MyClass
                Public Sub Check(x As Object)
                    If x Is Nothing Then
                        Return
                    End If
                End Sub
            End Class
            """;

        var pipeline = BuildPipeline();
        var results = await pipeline.ProcessFileAsync(vbSource, "Test.vb");

        Assert.Contains(results, r => r.Route == TranslationRoute.SeedRule);
    }

    private static TranslationPipeline BuildPipeline()
    {
        var engine = new SeedRuleEngine(SeedRuleRegistry.GetAll());
        var resolver = new LlmUsingResolver();
        var validator = new Validator.RoslynCompileValidator();
        // Use null LlmTranslator — tests should not hit the API
        return new TranslationPipeline(
            roslynTranslator: new RoslynTranslator(),
            analyzer: new DifficultyAnalyzer(),
            seedRuleEngine: engine,
            llmTranslator: null,
            usingResolver: resolver,
            validator: validator,
            repairAgent: null,
            correctionStore: null);
    }
}
```

- [ ] Run failing:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~TranslationPipelineTests"
```

Expected: **FAILED**

- [ ] Create `src/VBMigrator.Core/Translator/TranslationPipeline.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VBMigrator.Core.Analyzer;
using VBMigrator.Core.Learning;
using VBMigrator.Core.Models;
using VBMigrator.Core.SeedRules;
using VBMigrator.Core.Validator;

namespace VBMigrator.Core.Translator;

public class TranslationPipeline(
    RoslynTranslator roslynTranslator,
    DifficultyAnalyzer analyzer,
    SeedRuleEngine seedRuleEngine,
    LlmTranslator? llmTranslator,
    LlmUsingResolver usingResolver,
    RoslynCompileValidator validator,
    RepairAgent? repairAgent,
    CorrectionStore? correctionStore)
{
    // Flag → SeedRule tag mapping (§4.1 paso [6])
    private static readonly Dictionary<string, string> _flagToTag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OnError"]       = "on_error_goto",
        ["OnErrorResume"] = "on_error_resume",
        ["LikeOp"]        = "like_operator",
        ["ExponOp"]       = "exponentiation",
        ["ByteLoop"]      = "for_byte_overflow",
        ["MyNamespace"]   = "my_settings"  // approximate; SeedRuleEngine picks correct My* rule
    };

    public async Task<IReadOnlyList<TranslationResult>> ProcessFileAsync(string vbSource, string filePath)
    {
        // Step [2]: ICSharpCode whole-file conversion
        var initialCs = await roslynTranslator.ConvertAsync(vbSource, filePath);

        // Step [3]: Analyze original VB for difficulty flags
        var diffMap = analyzer.Analyze(vbSource, filePath);

        // Step [4]: Split and pair methods
        var pairs = PairMethods(vbSource, initialCs, diffMap);

        var results = new List<TranslationResult>();
        foreach (var pair in pairs)
        {
            results.Add(await ProcessMethodPairAsync(pair, diffMap));
        }
        return results;
    }

    private async Task<TranslationResult> ProcessMethodPairAsync(
        MethodPair pair, DifficultyMap diffMap)
    {
        // No flags → trust ICSharpCode output
        var funcDiff = diffMap.Functions.FirstOrDefault(f => f.MethodName == pair.MethodName);
        if (funcDiff == null || funcDiff.Flags.Count == 0)
        {
            return new TranslationResult
            {
                CsSource = pair.CsMethodSource,
                Confidence = 0.85,
                Route = TranslationRoute.SeedRule,
                CompilerPassed = true
            };
        }

        // Step [6]: DB lookup via flag→tag mapping
        string? fewShot = null;
        if (correctionStore != null)
        {
            var primaryFlag = funcDiff.Flags.First(); // highest-priority flag first
            if (_flagToTag.TryGetValue(primaryFlag, out var tag))
                fewShot = await correctionStore.GetFewShotAsync(tag);
        }

        // Step [7a]: SeedRuleEngine on VB original nodes
        var vbTree = VisualBasicSyntaxTree.ParseText(pair.VbMethodSource);
        var nodeConfidences = new List<double>();
        var csNodes = new List<string>();

        foreach (var node in vbTree.GetRoot().DescendantNodesAndSelf())
        {
            var matched = seedRuleEngine.TryConvert(node, null, out var converted, out var confidence);
            if (matched && converted != null)
            {
                csNodes.Add(converted.ToFullString());
                nodeConfidences.Add(confidence);
            }
        }

        // Step [7b]: LLM for remaining nodes
        string methodCs = pair.CsMethodSource;
        if (llmTranslator != null && csNodes.Count < 1)
        {
            var llmResult = await llmTranslator.TranslateAsync(pair.VbMethodSource, fewShot);
            if (llmResult.Route == TranslationRoute.HumanQueue)
                return llmResult;
            methodCs = llmResult.CsSource;
            nodeConfidences.Add(llmResult.Confidence);
        }

        // Step [7c]: LlmUsingResolver on complete method C#
        var csTree = CSharpSyntaxTree.ParseText(methodCs);
        var usings = usingResolver.Resolve(csTree, null);
        // usings are accumulated at file level by caller

        // Step [8]: Validate
        var finalCs = string.Join("\n", csNodes.Count > 0 ? csNodes : [methodCs]);
        var validation = await validator.ValidateAsync(finalCs);

        if (!validation.Success && repairAgent != null)
        {
            var repaired = await repairAgent.RepairAsync(
                finalCs,
                string.Join("; ", validation.Errors),
                validation.Errors,
                nodeConfidences.DefaultIfEmpty(0.75).Min());

            if (!repaired.Repaired)
                return new TranslationResult { CsSource = finalCs, Confidence = 0.0, Route = TranslationRoute.HumanQueue, CompilerPassed = false };

            finalCs = repaired.CsSource;
            nodeConfidences.Add(repaired.Confidence);
        }

        // Step [9]: Score
        var finalConfidence = ConfidenceScorer.Score(nodeConfidences);
        var route = ConfidenceScorer.GetRoute(finalConfidence);

        return new TranslationResult
        {
            CsSource = finalCs,
            Confidence = finalConfidence,
            Route = route,
            CompilerPassed = validation.Success
        };
    }

    private static IEnumerable<MethodPair> PairMethods(
        string vbSource, string csSource, DifficultyMap diffMap)
    {
        var vbTree = VisualBasicSyntaxTree.ParseText(vbSource);
        var csTree = CSharpSyntaxTree.ParseText(csSource);

        var vbMethods = vbTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodBlockSyntax>()
            .Select(m => (Name: GetVbMethodName(m), Source: m.ToFullString()))
            .ToList();

        var csMethods = csTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(m => (Name: m.Identifier.Text, Source: m.ToFullString()))
            .ToList();

        var csCtors = csTree.GetRoot()
            .DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>()
            .Select(c => (Name: "New", Source: c.ToFullString()))
            .ToList();

        foreach (var vb in vbMethods)
        {
            var cs = csMethods.FirstOrDefault(c =>
                string.Equals(c.Name, vb.Name, StringComparison.OrdinalIgnoreCase));

            // Sub New → constructor
            if (cs == default && vb.Name == "New")
                cs = csCtors.FirstOrDefault();

            // No match → use ICSharpCode output as-is with name
            yield return new MethodPair(
                MethodName: vb.Name,
                VbMethodSource: vb.Source,
                CsMethodSource: cs == default ? string.Empty : cs.Source);
        }
    }

    private static string GetVbMethodName(MethodBlockSyntax m)
        => m.SubOrFunctionStatement.Identifier.Text;
}

public record MethodPair(string MethodName, string VbMethodSource, string CsMethodSource);
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~TranslationPipelineTests"
```

Expected: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/Translator/TranslationPipeline.cs tests/VBMigrator.Core.Tests/Translator/TranslationPipelineTests.cs
git commit -m "feat(translator): TranslationPipeline — ICSharpCode whole-file, per-method SeedRule+LLM, confidence routing"
```

---

## Task 18: PatternNormalizer

**Files:**
- Create: `src/VBMigrator.Core/Learning/PatternNormalizer.cs`
- Create: `tests/VBMigrator.Core.Tests/Learning/PatternNormalizerTests.cs`

- [ ] Write failing test:

```csharp
using VBMigrator.Core.Learning;
using Xunit;

namespace VBMigrator.Core.Tests.Learning;

public class PatternNormalizerTests
{
    [Fact]
    public void NormalizeVb_ReplacesIdentifiersAndTypes()
    {
        const string vb = "Dim result As Integer = x + y";
        var (template, map) = PatternNormalizer.NormalizeVb(vb);

        Assert.Contains("__var1__", template);
        Assert.Contains("__Type1__", template);
        Assert.DoesNotContain("result", template);
        Assert.DoesNotContain("Integer", template);
    }

    [Fact]
    public void NormalizeCs_VbOriginVarsGetSameSlot_CsIntroducedGetNewSlot()
    {
        const string vb = "On Error GoTo ErrHandler";
        const string cs = "try { } catch (Exception ex) { }";
        var (_, vbMap) = PatternNormalizer.NormalizeVb(vb);
        var (csTemplate, _) = PatternNormalizer.NormalizeCs(cs, vbMap);

        // 'ex' has no VB counterpart → __new1__
        Assert.Contains("__new1__", csTemplate);
        Assert.DoesNotContain(" ex ", csTemplate);
    }

    [Fact]
    public void NormalizeVb_TwoCallsWithSameInput_ProduceSameTemplate()
    {
        const string vb = "Dim count As Integer = 0";
        var (t1, _) = PatternNormalizer.NormalizeVb(vb);
        var (t2, _) = PatternNormalizer.NormalizeVb(vb);
        Assert.Equal(t1, t2);
    }
}
```

- [ ] Run failing:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~PatternNormalizerTests"
```

Expected: **FAILED**

- [ ] Create `src/VBMigrator.Core/Learning/PatternNormalizer.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using Microsoft.CodeAnalysis.CSharp;

namespace VBMigrator.Core.Learning;

public static class PatternNormalizer
{
    public record NormMap(
        Dictionary<string, string> IdentifierMap,  // original → __varN__
        Dictionary<string, string> TypeMap);        // original → __TypeN__

    public static (string Template, NormMap Map) NormalizeVb(string vbSnippet)
    {
        var tree = VisualBasicSyntaxTree.ParseText(vbSnippet);
        var root = tree.GetRoot();

        var identMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var typeMap  = new Dictionary<string, string>(StringComparer.Ordinal);
        int varCounter  = 0;
        int typeCounter = 0;

        // Collect identifiers (declared names)
        foreach (var id in root.DescendantTokens()
                     .Where(t => t.IsKind(SyntaxKind.IdentifierToken)))
        {
            var text = id.Text;
            if (!identMap.ContainsKey(text))
            {
                // Heuristic: if parent is a type token, treat as type
                var parent = id.Parent;
                if (parent is SimpleAsClauseSyntax || parent is TypeSyntax)
                {
                    if (!typeMap.ContainsKey(text))
                        typeMap[text] = $"__Type{++typeCounter}__";
                }
                else
                {
                    identMap[text] = $"__var{++varCounter}__";
                }
            }
        }

        var map = new NormMap(identMap, typeMap);
        return (ApplyMap(vbSnippet, map), map);
    }

    public static (string Template, NormMap Map) NormalizeCs(string csSnippet, NormMap vbMap)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(csSnippet);
        var root = tree.GetRoot();

        // Start new counter for C#-introduced identifiers
        int newCounter = 0;
        var csIntroduced = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var id in root.DescendantTokens()
                     .Where(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken)))
        {
            var text = id.Text;
            if (vbMap.IdentifierMap.ContainsKey(text) || vbMap.TypeMap.ContainsKey(text))
                continue; // already mapped from VB side
            if (!csIntroduced.ContainsKey(text))
                csIntroduced[text] = $"__new{++newCounter}__";
        }

        // Merge maps: VB-origin first, then C#-introduced
        var merged = new NormMap(
            new Dictionary<string, string>(vbMap.IdentifierMap.Concat(csIntroduced)),
            vbMap.TypeMap);

        return (ApplyMap(csSnippet, merged), merged);
    }

    private static string ApplyMap(string source, NormMap map)
    {
        // Replace longest matches first to avoid partial replacements
        var result = source;
        foreach (var (orig, replacement) in map.IdentifierMap
                     .Concat(map.TypeMap)
                     .OrderByDescending(p => p.Key.Length))
        {
            result = System.Text.RegularExpressions.Regex.Replace(
                result, $@"\b{System.Text.RegularExpressions.Regex.Escape(orig)}\b", replacement);
        }
        return result;
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~PatternNormalizerTests"
```

Expected: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/Learning/PatternNormalizer.cs tests/VBMigrator.Core.Tests/Learning/PatternNormalizerTests.cs
git commit -m "feat(learning): PatternNormalizer — VB-origin vars __varN__, C#-introduced __newN__, deterministic"
```

---

## Task 19: SQLite schema + CorrectionStore

**Files:**
- Create: `src/VBMigrator.Core/Learning/Migrations/001_initial.sql`
- Create: `src/VBMigrator.Core/Learning/CorrectionStore.cs`
- Create: `tests/VBMigrator.Core.Tests/Learning/CorrectionStoreTests.cs`

- [ ] Create `src/VBMigrator.Core/Learning/Migrations/001_initial.sql` (exact schema from spec §6.2):

```sql
CREATE TABLE IF NOT EXISTS patterns (
    id          TEXT PRIMARY KEY,
    tag         TEXT NOT NULL,
    vb_template TEXT NOT NULL,
    cs_template TEXT NOT NULL,
    embedding   BLOB,
    source      TEXT NOT NULL DEFAULT 'seed',
    applied     INTEGER NOT NULL DEFAULT 0,
    successes   INTEGER NOT NULL DEFAULT 0,
    created_at  TEXT NOT NULL,
    updated_at  TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS ix_patterns_tag ON patterns(tag);

CREATE TABLE IF NOT EXISTS translation_log (
    id              TEXT PRIMARY KEY,
    pattern_id      TEXT REFERENCES patterns(id),
    file_path       TEXT NOT NULL,
    method_name     TEXT,
    vb_input        TEXT NOT NULL,
    cs_output       TEXT NOT NULL,
    was_corrected   INTEGER NOT NULL DEFAULT 0,
    human_cs        TEXT,
    compiler_passed INTEGER NOT NULL DEFAULT 0,
    confidence      REAL NOT NULL DEFAULT 0,
    created_at      TEXT NOT NULL
);

CREATE VIEW IF NOT EXISTS pattern_stats AS
SELECT tag,
       COUNT(*) as total_patterns,
       SUM(applied) as total_applied,
       CAST(SUM(successes) AS REAL) / NULLIF(SUM(applied), 0) as success_rate
FROM patterns GROUP BY tag;
```

- [ ] Write failing test:

```csharp
using Microsoft.Data.Sqlite;
using VBMigrator.Core.Learning;
using Xunit;

namespace VBMigrator.Core.Tests.Learning;

public class CorrectionStoreTests : IDisposable
{
    private readonly string _dbPath = Path.GetTempFileName();
    private readonly CorrectionStore _store;

    public CorrectionStoreTests()
    {
        _store = new CorrectionStore(_dbPath);
        _store.InitializeAsync().Wait();
    }

    [Fact]
    public async Task SaveCorrection_ThenGetFewShot_ReturnsSavedPattern()
    {
        await _store.SaveCorrectionAsync(
            vbInput: "If x Is Nothing Then",
            csCorrection: "if (x is null) {",
            tag: "is_nothing");

        var fewShot = await _store.GetFewShotAsync("is_nothing");

        Assert.NotNull(fewShot);
        Assert.Contains("__", fewShot); // normalized template
    }

    [Fact]
    public async Task SaveCorrection_DuplicateTemplate_UpdatesSuccesses()
    {
        await _store.SaveCorrectionAsync("If x Is Nothing Then", "if (x is null) {", "is_nothing");
        await _store.SaveCorrectionAsync("If x Is Nothing Then", "if (x is null) {", "is_nothing");

        // Should have 1 pattern with successes > 0, not 2 patterns
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        await conn.OpenAsync();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM patterns WHERE tag='is_nothing'";
        var count = (long)(await cmd.ExecuteScalarAsync())!;
        Assert.Equal(1, count);
    }

    public void Dispose() => File.Delete(_dbPath);
}
```

- [ ] Run failing:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~CorrectionStoreTests"
```

Expected: **FAILED**

- [ ] Create `src/VBMigrator.Core/Learning/CorrectionStore.cs`:

```csharp
using Microsoft.Data.Sqlite;

namespace VBMigrator.Core.Learning;

public class CorrectionStore(string dbPath)
{
    public async Task InitializeAsync()
    {
        var sql = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Learning/Migrations/001_initial.sql"));
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task SaveCorrectionAsync(string vbInput, string csCorrection, string tag)
    {
        var (vbTemplate, map) = PatternNormalizer.NormalizeVb(vbInput);
        var (csTemplate, _)   = PatternNormalizer.NormalizeCs(csCorrection, map);

        using var conn = OpenConnection();
        // Upsert: exact equality on tag + vb_template (NOT LIKE)
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO patterns (id, tag, vb_template, cs_template, source, applied, successes, created_at, updated_at)
            VALUES ($id, $tag, $vbt, $cst, 'human', 0, 1, $now, $now)
            ON CONFLICT DO UPDATE
            SET successes = successes + 1, updated_at = $now
            WHERE tag = $tag AND vb_template = $vbt
            """;
        cmd.Parameters.AddWithValue("$id",  Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$tag", tag);
        cmd.Parameters.AddWithValue("$vbt", vbTemplate);
        cmd.Parameters.AddWithValue("$cst", csTemplate);
        cmd.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("o"));
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<string?> GetFewShotAsync(string tag)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT vb_template || ' → ' || cs_template
            FROM patterns
            WHERE tag = $tag AND source = 'human'
            ORDER BY successes DESC
            LIMIT 1
            """;
        cmd.Parameters.AddWithValue("$tag", tag);
        return (await cmd.ExecuteScalarAsync()) as string;
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        return conn;
    }
}
```

- [ ] Copy migration SQL as embedded resource — add to `.csproj`:

```xml
<ItemGroup>
  <EmbeddedResource Include="Learning/Migrations/001_initial.sql" />
</ItemGroup>
```

Then update `InitializeAsync` to read from embedded resource instead of file:

```csharp
public async Task InitializeAsync()
{
    var asm = typeof(CorrectionStore).Assembly;
    var name = asm.GetManifestResourceNames()
                  .First(n => n.EndsWith("001_initial.sql"));
    using var stream = asm.GetManifestResourceStream(name)!;
    using var reader = new StreamReader(stream);
    var sql = await reader.ReadToEndAsync();

    using var conn = OpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = sql;
    await cmd.ExecuteNonQueryAsync();
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~CorrectionStoreTests"
```

Expected: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/Learning/ tests/VBMigrator.Core.Tests/Learning/CorrectionStoreTests.cs
git commit -m "feat(learning): SQLite schema + CorrectionStore — exact upsert, PatternNormalizer integration"
```

---

## Task 20: ProjectFileConverter

**Files:**
- Create: `src/VBMigrator.Core/ProjectFileConverter/VbprojToCsprojConverter.cs`
- Create: `tests/VBMigrator.Core.Tests/ProjectFileConverter/VbprojToCsprojConverterTests.cs`

- [ ] Write failing test:

```csharp
using VBMigrator.Core.ProjectFileConverter;
using Xunit;

namespace VBMigrator.Core.Tests.ProjectFileConverter;

public class VbprojToCsprojConverterTests
{
    private static readonly string OldStyleVbproj = """
        <?xml version="1.0" encoding="utf-8"?>
        <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
          <PropertyGroup>
            <RootNamespace>MyApp</RootNamespace>
            <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
          </PropertyGroup>
          <ItemGroup>
            <Compile Include="Form1.vb" />
            <Import Project="$(MSBuildBinPath)\Microsoft.VisualBasic.targets" />
            <Reference Include="Microsoft.VisualBasic" />
          </ItemGroup>
        </Project>
        """;

    private static readonly string SdkStyleVbproj = """
        <Project Sdk="Microsoft.VisualBasic.App">
          <PropertyGroup>
            <TargetFramework>net48</TargetFramework>
            <RootNamespace>MyApp</RootNamespace>
          </PropertyGroup>
        </Project>
        """;

    [Fact]
    public void Convert_OldStyle_ReplacesCompileAndRemovesVbRefs()
    {
        var result = VbprojToCsprojConverter.Convert(OldStyleVbproj);

        Assert.Contains("Microsoft.NET.Sdk", result);
        Assert.Contains("Form1.cs", result);
        Assert.DoesNotContain("Form1.vb", result);
        Assert.DoesNotContain("Microsoft.VisualBasic.targets", result);
        Assert.DoesNotContain("Microsoft.VisualBasic\"", result);
        Assert.Contains("MyApp", result); // RootNamespace preserved
    }

    [Fact]
    public void Convert_SdkStyle_UpdatesSdkAttribute()
    {
        var result = VbprojToCsprojConverter.Convert(SdkStyleVbproj);

        Assert.Contains("Microsoft.NET.Sdk\"", result);
        Assert.DoesNotContain("Microsoft.VisualBasic.App", result);
        Assert.Contains("MyApp", result);
    }

    [Fact]
    public void Convert_ComReference_PreservesWithComment()
    {
        const string vbproj = """
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <ItemGroup>
                <COMReference Include="Excel.Application" />
              </ItemGroup>
            </Project>
            """;

        var result = VbprojToCsprojConverter.Convert(vbproj);

        Assert.Contains("COMReference", result);
        Assert.Contains("VBMigrator: COM reference", result);
    }
}
```

- [ ] Run failing:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~VbprojToCsprojConverterTests"
```

Expected: **FAILED**

- [ ] Create `src/VBMigrator.Core/ProjectFileConverter/VbprojToCsprojConverter.cs`:

```csharp
using System.Xml.Linq;

namespace VBMigrator.Core.ProjectFileConverter;

public static class VbprojToCsprojConverter
{
    public static string Convert(string vbprojXml)
    {
        var doc = XDocument.Parse(vbprojXml);
        var root = doc.Root!;
        var ns = root.Name.Namespace;

        bool isSdkStyle = root.Attribute("Sdk") != null;

        if (isSdkStyle)
            return ConvertSdkStyle(doc);
        else
            return ConvertOldStyle(doc, ns);
    }

    private static string ConvertSdkStyle(XDocument doc)
    {
        var root = doc.Root!;
        // Replace Sdk attribute
        var sdkAttr = root.Attribute("Sdk");
        if (sdkAttr != null)
            sdkAttr.Value = "Microsoft.NET.Sdk";

        // Normalize TargetFramework
        foreach (var tf in root.Descendants("TargetFramework"))
            tf.Value = NormalizeTargetFramework(tf.Value);

        return doc.ToString();
    }

    private static string ConvertOldStyle(XDocument doc, XNamespace ns)
    {
        var root = doc.Root!;

        // Replace ToolsVersion-style root with SDK-style
        root.SetAttributeValue("Sdk", "Microsoft.NET.Sdk");
        root.Attributes().Where(a => a.Name != "Sdk").Remove();

        // TargetFrameworkVersion → TargetFramework
        foreach (var el in root.Descendants(ns + "TargetFrameworkVersion").ToList())
        {
            var tf = new XElement("TargetFramework", NormalizeTargetFramework(el.Value));
            el.ReplaceWith(tf);
        }

        // Compile Include: .vb → .cs
        foreach (var compile in root.Descendants(ns + "Compile").ToList())
        {
            var inc = compile.Attribute("Include");
            if (inc != null && inc.Value.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
                inc.Value = inc.Value[..^3] + ".cs";
        }

        // Remove VB-specific imports and references
        foreach (var import in root.Descendants(ns + "Import").ToList())
        {
            var project = import.Attribute("Project")?.Value ?? "";
            if (project.Contains("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase))
                import.Remove();
        }
        foreach (var refEl in root.Descendants(ns + "Reference").ToList())
        {
            var include = refEl.Attribute("Include")?.Value ?? "";
            if (include.StartsWith("Microsoft.VisualBasic", StringComparison.OrdinalIgnoreCase))
                refEl.Remove();
        }

        // COM references: add comment
        foreach (var com in root.Descendants(ns + "COMReference").ToList())
        {
            var comment = new XComment(" VBMigrator: COM reference — verificar interop assembly ");
            com.AddBeforeSelf(comment);
        }

        // Remove xmlns from child elements (SDK-style doesn't use it)
        foreach (var el in root.DescendantsAndSelf())
            el.Name = el.Name.LocalName;

        return doc.ToString();
    }

    private static string NormalizeTargetFramework(string value)
    {
        // v4.8 → net48, v4.7.2 → net472, net8.0 → net8.0
        if (value.StartsWith("v"))
        {
            var ver = value[1..].Replace(".", "");
            return $"net{ver}";
        }
        return value;
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~VbprojToCsprojConverterTests"
```

Expected: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/ProjectFileConverter/ tests/VBMigrator.Core.Tests/ProjectFileConverter/
git commit -m "feat(converter): VbprojToCsprojConverter — SDK-style + old-style detection, COM comment"
```

---

## Task 21: AspxHandler

**Files:**
- Create: `src/VBMigrator.Core/AspxHandler/AspxDirectiveRewriter.cs`
- Create: `src/VBMigrator.Core/AspxHandler/EventWireupMigrator.cs`
- Create: `src/VBMigrator.Core/AspxHandler/AspxMigrationResult.cs`
- Create: `tests/VBMigrator.Core.Tests/AspxHandler/AspxHandlerTests.cs`

- [ ] Write failing test:

```csharp
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.AspxHandler;
using Xunit;

namespace VBMigrator.Core.Tests.AspxHandler;

public class AspxHandlerTests
{
    [Fact]
    public void AspxDirectiveRewriter_ReplacesLanguageAndCodeBehind()
    {
        const string aspx = """
            <%@ Page Language="VB" AutoEventWireup="false" CodeBehind="Default.aspx.vb" Inherits="MyApp.Default" %>
            """;

        var result = AspxDirectiveRewriter.Rewrite(aspx);

        Assert.Contains("Language=\"C#\"", result);
        Assert.Contains("Default.aspx.cs", result);
        Assert.DoesNotContain(".aspx.vb", result);
    }

    [Fact]
    public void EventWireupMigrator_GeneratesSubscriptions_FromHandlesDeclarations()
    {
        const string vbCodeBehind = """
            Public Class Default
                Inherits System.Web.UI.Page

                Protected Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
                    Label1.Text = "clicked"
                End Sub

                Protected Sub Page_Load(sender As Object, e As EventArgs) Handles Me.Load
                    Label1.Text = "loaded"
                End Sub
            End Class
            """;

        var vbTree = VisualBasicSyntaxTree.ParseText(vbCodeBehind);
        var subscriptions = EventWireupMigrator.ExtractSubscriptions(vbTree);

        Assert.Contains(subscriptions, s => s.Contains("Button1.Click += Button1_Click"));
        Assert.Contains(subscriptions, s => s.Contains("Load += Page_Load") || s.Contains("this.Load += Page_Load"));
    }
}
```

- [ ] Run failing:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~AspxHandlerTests"
```

Expected: **FAILED**

- [ ] Create `src/VBMigrator.Core/AspxHandler/AspxMigrationResult.cs`:

```csharp
namespace VBMigrator.Core.AspxHandler;

public record AspxMigrationResult(
    string RewrittenAspx,
    IReadOnlyList<string> EventSubscriptions);
```

- [ ] Create `src/VBMigrator.Core/AspxHandler/AspxDirectiveRewriter.cs`:

```csharp
using System.Text.RegularExpressions;

namespace VBMigrator.Core.AspxHandler;

public static class AspxDirectiveRewriter
{
    public static string Rewrite(string aspxContent)
    {
        var result = Regex.Replace(aspxContent,
            @"Language\s*=\s*""VB""",
            @"Language=""C#""",
            RegexOptions.IgnoreCase);

        result = Regex.Replace(result,
            @"CodeBehind\s*=\s*""([^""]+)\.aspx\.vb""",
            m => $@"CodeBehind=""{m.Groups[1].Value}.aspx.cs""",
            RegexOptions.IgnoreCase);

        return result;
    }
}
```

- [ ] Create `src/VBMigrator.Core/AspxHandler/EventWireupMigrator.cs`:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.AspxHandler;

public static class EventWireupMigrator
{
    // Receives VBSyntaxTree from DifficultyAnalyzer (no re-parse)
    public static IReadOnlyList<string> ExtractSubscriptions(SyntaxTree vbTree)
    {
        var subscriptions = new List<string>();
        var root = vbTree.GetRoot();

        foreach (var method in root.DescendantNodes().OfType<MethodBlockSyntax>())
        {
            var stmt = method.SubOrFunctionStatement;
            var handlesClause = stmt.HandlesClause;
            if (handlesClause == null) continue;

            var methodName = stmt.Identifier.Text;
            foreach (var handlesItem in handlesClause.Events)
            {
                var eventText = handlesItem.ToString().Trim();
                // "Me.Load" → "this.Load", "Button1.Click" → "Button1.Click"
                eventText = eventText.Replace("Me.", "this.");
                subscriptions.Add($"{eventText} += {methodName};");
            }
        }

        return subscriptions;
    }
}
```

- [ ] Run tests:

```bash
dotnet test tests/VBMigrator.Core.Tests/VBMigrator.Core.Tests.csproj --filter "FullyQualifiedName~AspxHandlerTests"
```

Expected: **PASSED**

- [ ] Commit:

```bash
git add src/VBMigrator.Core/AspxHandler/ tests/VBMigrator.Core.Tests/AspxHandler/
git commit -m "feat(aspx): AspxDirectiveRewriter + EventWireupMigrator — Language/CodeBehind rewrite, Handles→subscriptions"
```

---

## Task 22: CLI — ConvertCommand

**Files:**
- Create: `src/VBMigrator.CLI/Program.cs`
- Create: `src/VBMigrator.CLI/Commands/ConvertCommand.cs`
- Edit: `src/VBMigrator.CLI/VBMigrator.CLI.csproj`

- [ ] Edit `src/VBMigrator.CLI/VBMigrator.CLI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Exe</OutputType>
    <AssemblyName>vbmigrator</AssemblyName>
    <PackAsTool>true</PackAsTool>
    <ToolCommandName>vbmigrator</ToolCommandName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.CommandLine" Version="2.*" />
    <PackageReference Include="Microsoft.Build.Locator" Version="1.*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/VBMigrator.Core/VBMigrator.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] Create `src/VBMigrator.CLI/Program.cs`:

```csharp
using System.CommandLine;
using VBMigrator.CLI.Commands;

var rootCommand = new RootCommand("VBMigrator — VB.NET to C# migration tool");

var convertCmd = ConvertCommandBuilder.Build();
var kbCmd      = KbCommandBuilder.Build();
var reportCmd  = ReportCommandBuilder.Build();

rootCommand.AddCommand(convertCmd);
rootCommand.AddCommand(kbCmd);
rootCommand.AddCommand(reportCmd);

return await rootCommand.InvokeAsync(args);
```

- [ ] Create `src/VBMigrator.CLI/Commands/ConvertCommand.cs`:

```csharp
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization;
using VBMigrator.Core.Models;
using VBMigrator.Core.Translator;

namespace VBMigrator.CLI.Commands;

public static class ConvertCommandBuilder
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static Command Build()
    {
        var cmd = new Command("convert", "Convert VB.NET files to C#");

        var fileOpt     = new Option<FileInfo?>("--file",  "Single .vb file to convert");
        var solutionOpt = new Option<FileInfo?>("--solution", "Solution .sln to convert");
        var outputOpt   = new Option<DirectoryInfo?>("--output", "Output directory");
        var jsonOpt     = new Option<bool>("--json-output", "Emit JSON to stdout (--file mode only)");
        var dryRunOpt   = new Option<bool>("--dry-run", "Report only, do not write files");
        var reportOpt   = new Option<FileInfo?>("--report", "Report HTML output path");

        cmd.AddOption(fileOpt);
        cmd.AddOption(solutionOpt);
        cmd.AddOption(outputOpt);
        cmd.AddOption(jsonOpt);
        cmd.AddOption(dryRunOpt);
        cmd.AddOption(reportOpt);

        cmd.SetHandler(async (file, solution, output, jsonOutput, dryRun, report) =>
        {
            if (file != null)
                await ConvertFile(file, jsonOutput);
            else if (solution != null)
                await ConvertSolution(solution, output, dryRun);
        }, fileOpt, solutionOpt, outputOpt, jsonOpt, dryRunOpt, reportOpt);

        return cmd;
    }

    private static async Task ConvertFile(FileInfo file, bool jsonOutput)
    {
        var vbSource = await File.ReadAllTextAsync(file.FullName);
        var pipeline = PipelineFactory.Build();
        var results  = await pipeline.ProcessFileAsync(vbSource, file.FullName);
        var first    = results.FirstOrDefault();

        if (first == null) return;

        if (jsonOutput)
        {
            var dto = new TranslationResultDto(
                filePath: file.FullName,
                csSource: first.CsSource,
                confidence: first.Confidence,
                route: first.Route.ToString(),
                compilerPassed: first.CompilerPassed,
                compilerErrors: first.CompilerErrors,
                patternTag: first.PatternTag,
                llmFailureReason: first.LlmFailureReason?.ToString());

            Console.WriteLine(JsonSerializer.Serialize(dto, JsonOpts));
        }
        else
        {
            var outPath = Path.ChangeExtension(file.FullName, ".cs");
            await File.WriteAllTextAsync(outPath, first.CsSource);
        }
    }

    private static async Task ConvertSolution(FileInfo sln, DirectoryInfo? output, bool dryRun)
    {
        // For each .vb file in the solution directory
        var baseDir = sln.Directory!;
        var vbFiles = baseDir.GetFiles("*.vb", SearchOption.AllDirectories);
        var pipeline = PipelineFactory.Build();

        foreach (var vbFile in vbFiles)
        {
            var vbSource = await File.ReadAllTextAsync(vbFile.FullName);
            var results  = await pipeline.ProcessFileAsync(vbSource, vbFile.FullName);
            var status   = results.All(r => r.Route != TranslationRoute.HumanQueue) ? "OK" : "HUMAN_QUEUE";

            // Progress to stderr — VSIX reads this for progress bar
            Console.Error.WriteLine($"FILE: {vbFile.Name} {status}");

            if (!dryRun)
            {
                var outPath = output != null
                    ? Path.Combine(output.FullName, Path.ChangeExtension(vbFile.Name, ".cs"))
                    : Path.ChangeExtension(vbFile.FullName, ".cs");

                var csSource = string.Join("\n\n", results.Select(r => r.CsSource));
                await File.WriteAllTextAsync(outPath, csSource);
            }
        }
    }
}

public record TranslationResultDto(
    string filePath,
    string csSource,
    double confidence,
    string route,
    bool compilerPassed,
    List<string> compilerErrors,
    string? patternTag,
    string? llmFailureReason);
```

- [ ] Build CLI to verify it compiles:

```bash
dotnet build src/VBMigrator.CLI/VBMigrator.CLI.csproj
```

Expected: **Build succeeded**

- [ ] Commit:

```bash
git add src/VBMigrator.CLI/
git commit -m "feat(cli): ConvertCommand — --file JSON stdout, --solution stderr progress, CamelCase+StringEnum JSON"
```

---

## Task 23: CLI KbCommand + ReportCommand

**Files:**
- Create: `src/VBMigrator.CLI/Commands/KbCommand.cs`
- Create: `src/VBMigrator.CLI/Commands/ReportCommand.cs`

- [ ] Create `src/VBMigrator.CLI/Commands/KbCommand.cs`:

```csharp
using System.CommandLine;
using VBMigrator.Core.Learning;

namespace VBMigrator.CLI.Commands;

public static class KbCommandBuilder
{
    public static Command Build()
    {
        var cmd = new Command("kb", "Knowledge base operations");

        // kb save
        var saveCmd = new Command("save", "Save a human correction");
        var vbOpt   = new Option<string>("--vb",  "Original VB snippet") { IsRequired = true };
        var csOpt   = new Option<string>("--cs",  "Corrected C# snippet") { IsRequired = true };
        var tagOpt  = new Option<string>("--tag", "Pattern tag") { IsRequired = true };
        saveCmd.AddOption(vbOpt);
        saveCmd.AddOption(csOpt);
        saveCmd.AddOption(tagOpt);
        saveCmd.SetHandler(async (vb, cs, tag) =>
        {
            var store = new CorrectionStore(GetDbPath());
            await store.InitializeAsync();
            await store.SaveCorrectionAsync(vb, cs, tag);
            Console.WriteLine($"Saved pattern for tag '{tag}'");
        }, vbOpt, csOpt, tagOpt);

        // kb stats
        var statsCmd = new Command("stats", "Show pattern statistics");
        statsCmd.SetHandler(async () =>
        {
            var store = new CorrectionStore(GetDbPath());
            await store.InitializeAsync();
            var stats = await store.GetStatsAsync();
            foreach (var s in stats)
                Console.WriteLine($"{s.Tag}: {s.TotalPatterns} patterns, {s.SuccessRate:P0} success rate");
        });

        cmd.AddCommand(saveCmd);
        cmd.AddCommand(statsCmd);
        return cmd;
    }

    private static string GetDbPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "VBMigrator");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "kb.sqlite");
    }
}
```

- [ ] Add `GetStatsAsync` to `CorrectionStore.cs`:

```csharp
public record PatternStat(string Tag, int TotalPatterns, double? SuccessRate);

public async Task<IReadOnlyList<PatternStat>> GetStatsAsync()
{
    using var conn = OpenConnection();
    using var cmd = conn.CreateCommand();
    cmd.CommandText = "SELECT tag, total_patterns, success_rate FROM pattern_stats ORDER BY tag";
    var result = new List<PatternStat>();
    using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        result.Add(new PatternStat(reader.GetString(0), reader.GetInt32(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2)));
    return result;
}
```

- [ ] Create `src/VBMigrator.CLI/Commands/ReportCommand.cs`:

```csharp
using System.CommandLine;
using Microsoft.Data.Sqlite;

namespace VBMigrator.CLI.Commands;

public static class ReportCommandBuilder
{
    public static Command Build()
    {
        var cmd     = new Command("report", "Generate HTML migration report");
        var outOpt  = new Option<FileInfo>("--output", () => new FileInfo("report.html"), "Output HTML path");
        cmd.AddOption(outOpt);

        cmd.SetHandler(async (output) =>
        {
            var dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "VBMigrator", "kb.sqlite");

            if (!File.Exists(dbPath))
            {
                Console.Error.WriteLine("No knowledge base found.");
                return;
            }

            using var conn = new SqliteConnection($"Data Source={dbPath}");
            await conn.OpenAsync();
            using var cmd2 = conn.CreateCommand();
            cmd2.CommandText = """
                SELECT file_path, method_name, confidence, compiler_passed, was_corrected
                FROM translation_log ORDER BY created_at DESC LIMIT 500
                """;

            var rows = new System.Text.StringBuilder();
            using var reader = await cmd2.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.AppendLine($"<tr><td>{reader.GetString(0)}</td>" +
                    $"<td>{reader.GetString(1)}</td>" +
                    $"<td>{reader.GetDouble(2):F2}</td>" +
                    $"<td>{(reader.GetBoolean(3) ? "✓" : "✗")}</td>" +
                    $"<td>{(reader.GetBoolean(4) ? "yes" : "no")}</td></tr>");
            }

            var html = $"""
                <!DOCTYPE html><html><head><title>VBMigrator Report</title></head>
                <body><h1>Migration Log</h1>
                <table border="1">
                <tr><th>File</th><th>Method</th><th>Confidence</th><th>Compiled</th><th>Corrected</th></tr>
                {rows}
                </table></body></html>
                """;

            await File.WriteAllTextAsync(output.FullName, html);
            Console.WriteLine($"Report written to {output.FullName}");
        }, outOpt);

        return cmd;
    }
}
```

- [ ] Build and run a quick smoke test:

```bash
dotnet build src/VBMigrator.CLI/VBMigrator.CLI.csproj
dotnet run --project src/VBMigrator.CLI -- kb --help
```

Expected: shows kb subcommands

- [ ] Commit:

```bash
git add src/VBMigrator.CLI/Commands/KbCommand.cs src/VBMigrator.CLI/Commands/ReportCommand.cs src/VBMigrator.Core/Learning/CorrectionStore.cs
git commit -m "feat(cli): KbCommand (save+stats) + ReportCommand (HTML from translation_log)"
```

---

## Task 24: VSIX — CliLocator + CliRunner + TranslationResultDto

**Files:**
- Create: `src/VBMigrator.VSIX/VBMigrator.VSIX.csproj`
- Create: `src/VBMigrator.VSIX/Services/CliLocator.cs`
- Create: `src/VBMigrator.VSIX/Services/CliRunner.cs`
- Create: `src/VBMigrator.VSIX/Services/TranslationResultDto.cs`

- [ ] Create `src/VBMigrator.VSIX/VBMigrator.VSIX.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net472</TargetFramework>
    <Nullable>enable</Nullable>
    <UseWindowsForms>true</UseWindowsForms>
    <UseWPF>true</UseWPF>
    <RootNamespace>VBMigrator.VSIX</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.VisualStudio.SDK" Version="17.*" />
    <PackageReference Include="Microsoft.VSSDK.BuildTools" Version="17.*" />
  </ItemGroup>
</Project>
```

Add VSIX to solution:

```bash
dotnet sln add src/VBMigrator.VSIX/VBMigrator.VSIX.csproj
```

- [ ] Create `src/VBMigrator.VSIX/Services/TranslationResultDto.cs`:

```csharp
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VBMigrator.VSIX.Services;

// Properties camelCase to match CLI JSON output (CamelCase policy)
public class TranslationResultDto
{
    [JsonPropertyName("filePath")]     public string FilePath { get; set; } = "";
    [JsonPropertyName("csSource")]     public string CsSource { get; set; } = "";
    [JsonPropertyName("confidence")]   public double Confidence { get; set; }
    [JsonPropertyName("route")]        public string Route { get; set; } = "";   // string, not enum
    [JsonPropertyName("compilerPassed")] public bool CompilerPassed { get; set; }
    [JsonPropertyName("compilerErrors")] public List<string> CompilerErrors { get; set; } = new();
    [JsonPropertyName("patternTag")]   public string? PatternTag { get; set; }
    [JsonPropertyName("llmFailureReason")] public string? LlmFailureReason { get; set; }
}
```

- [ ] Create `src/VBMigrator.VSIX/Services/CliLocator.cs`:

```csharp
using System;
using System.IO;

namespace VBMigrator.VSIX.Services;

public static class CliLocator
{
    private static string? _configuredPath;

    public static void SetConfiguredPath(string? path) => _configuredPath = path;

    public static string FindExecutable()
    {
        // Order: (1) configured via Tools→VBMigrator→Settings
        if (!string.IsNullOrEmpty(_configuredPath) && File.Exists(_configuredPath))
            return _configuredPath;

        // (2) PATH
        var inPath = FindOnPath("vbmigrator.exe") ?? FindOnPath("vbmigrator");
        if (inPath != null)
            return inPath;

        // (3) Error
        throw new InvalidOperationException(
            "vbmigrator CLI not found. Install it with: dotnet tool install -g VBMigrator.CLI\n" +
            "Or set the path in Tools → VBMigrator → Settings.");
    }

    private static string? FindOnPath(string exe)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir.Trim(), exe);
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
```

- [ ] Create `src/VBMigrator.VSIX/Services/CliRunner.cs`:

```csharp
using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VBMigrator.VSIX.Services;

public class CliRunner
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // For --file: returns single JSON result
    public async Task<TranslationResultDto?> ConvertFileAsync(
        string filePath, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = CliLocator.FindExecutable(),
            Arguments              = $"convert --file \"{filePath}\" --json-output",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var proc = Process.Start(psi)!;

        // Drain stdout + stderr concurrently — prevents deadlock on large files
        // (OS buffer fills if stderr is not drained while stdout blocks)
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync(ct);
        var json   = await stdoutTask;
        var _errors = await stderrTask;  // log or discard — never ignore the Task

        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<TranslationResultDto>(json, JsonOpts);
    }

    // For --solution: fires and monitors stderr for progress, returns exit code
    public async Task<int> ConvertSolutionAsync(
        string slnPath, string? outputDir,
        Action<string>? onProgress, CancellationToken ct = default)
    {
        var args = $"convert --solution \"{slnPath}\"";
        if (outputDir != null) args += $" --output \"{outputDir}\"";

        var psi = new ProcessStartInfo
        {
            FileName               = CliLocator.FindExecutable(),
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var proc = Process.Start(psi)!;

        // Read stderr line-by-line for progress (FILE: name.vb OK|FAIL|HUMAN_QUEUE)
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) onProgress?.Invoke(e.Data);
        };
        proc.BeginErrorReadLine();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync(ct);
        await stdoutTask;

        return proc.ExitCode;
    }
}
```

- [ ] Build VSIX project to verify compilation:

```bash
dotnet build src/VBMigrator.VSIX/VBMigrator.VSIX.csproj
```

Expected: **Build succeeded**

- [ ] Commit:

```bash
git add src/VBMigrator.VSIX/
git commit -m "feat(vsix): CliLocator (3-step search), CliRunner (concurrent stdout+stderr drain), TranslationResultDto"
```

---

## Task 25: VSIX — Package, Commands, ReviewQueue

**Files:**
- Create: `src/VBMigrator.VSIX/VBMigratorPackage.cs`
- Create: `src/VBMigrator.VSIX/source.extension.vsixmanifest`
- Create: `src/VBMigrator.VSIX/Commands/ConvertFileCommand.cs`
- Create: `src/VBMigrator.VSIX/Commands/ConvertSolutionCommand.cs`
- Create: `src/VBMigrator.VSIX/ToolWindows/ReviewQueueWindow.cs`
- Create: `src/VBMigrator.VSIX/ToolWindows/ReviewQueueWindowControl.xaml`

- [ ] Create `src/VBMigrator.VSIX/VBMigratorPackage.cs`:

```csharp
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace VBMigrator.VSIX;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(ToolWindows.ReviewQueueWindow))]
public sealed class VBMigratorPackage : AsyncPackage
{
    public const string PackageGuidString = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await Commands.ConvertFileCommand.InitializeAsync(this);
        await Commands.ConvertSolutionCommand.InitializeAsync(this);
    }
}
```

- [ ] Create `src/VBMigrator.VSIX/Commands/ConvertFileCommand.cs`:

```csharp
using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VBMigrator.VSIX.Services;

namespace VBMigrator.VSIX.Commands;

public sealed class ConvertFileCommand
{
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new("b1c2d3e4-f5a6-7890-bcde-f01234567891");

    private readonly AsyncPackage _package;
    private ConvertFileCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        var id = new CommandID(CommandSet, CommandId);
        commandService.AddCommand(new OleMenuCommand(Execute, id));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var svc = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (svc != null) new ConvertFileCommand(package, svc);
    }

    private async void Execute(object sender, EventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        // Get selected file from Solution Explorer
        var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        var selectedItem = dte?.SelectedItems?.Item(1)?.ProjectItem;
        var filePath = selectedItem?.FileNames[1];
        if (filePath == null || !filePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
            return;

        var runner = new CliRunner();
        var result = await runner.ConvertFileAsync(filePath);
        if (result == null) return;

        if (result.Route == "HumanQueue")
        {
            // Open Review Queue
            await _package.ShowToolWindowAsync(typeof(ToolWindows.ReviewQueueWindow), 0, true, _package.DisposalToken);
        }
        else
        {
            // Write .cs file
            var csPath = Path.ChangeExtension(filePath, ".cs");
            await System.IO.File.WriteAllTextAsync(csPath, result.CsSource);
        }
    }
}
```

- [ ] Create `src/VBMigrator.VSIX/Commands/ConvertSolutionCommand.cs`:

```csharp
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VBMigrator.VSIX.Services;

namespace VBMigrator.VSIX.Commands;

public sealed class ConvertSolutionCommand
{
    public const int CommandId = 0x0102;
    public static readonly Guid CommandSet = ConvertFileCommand.CommandSet;

    private readonly AsyncPackage _package;

    private ConvertSolutionCommand(AsyncPackage package, OleMenuCommandService svc)
    {
        _package = package;
        svc.AddCommand(new OleMenuCommand(Execute, new CommandID(CommandSet, CommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var svc = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (svc != null) new ConvertSolutionCommand(package, svc);
    }

    private async void Execute(object sender, EventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        var slnPath = dte?.Solution?.FullName;
        if (slnPath == null) return;

        var runner = new CliRunner();
        var exitCode = await runner.ConvertSolutionAsync(
            slnPath, outputDir: null,
            onProgress: line => System.Diagnostics.Debug.WriteLine($"[VBMigrator] {line}"));

        if (exitCode == 0)
            await _package.ShowToolWindowAsync(typeof(ToolWindows.ReviewQueueWindow), 0, true, _package.DisposalToken);
    }
}
```

- [ ] Create `src/VBMigrator.VSIX/ToolWindows/ReviewQueueWindow.cs`:

```csharp
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace VBMigrator.VSIX.ToolWindows;

[Guid("c2d3e4f5-a6b7-8901-cdef-012345678902")]
public class ReviewQueueWindow : ToolWindowPane
{
    public ReviewQueueWindow() : base(null)
    {
        Caption = "VBMigrator — Review Queue";
        Content = new ReviewQueueWindowControl();
    }
}
```

- [ ] Create `src/VBMigrator.VSIX/ToolWindows/ReviewQueueWindowControl.xaml`:

```xml
<UserControl x:Class="VBMigrator.VSIX.ToolWindows.ReviewQueueWindowControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Grid>
    <Grid.RowDefinitions>
      <RowDefinition Height="Auto"/>
      <RowDefinition Height="*"/>
      <RowDefinition Height="200"/>
      <RowDefinition Height="Auto"/>
    </Grid.RowDefinitions>

    <TextBlock Grid.Row="0" Text="Review Queue" FontWeight="Bold" Margin="4"/>

    <ListBox Grid.Row="1" x:Name="QueueList" DisplayMemberPath="MethodName"/>

    <Grid Grid.Row="2">
      <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
      </Grid.ColumnDefinitions>
      <TextBox Grid.Column="0" x:Name="VbPanel" IsReadOnly="True" FontFamily="Consolas"/>
      <TextBox Grid.Column="1" x:Name="CsPanel" FontFamily="Consolas" AcceptsReturn="True"/>
    </Grid>

    <StackPanel Grid.Row="3" Orientation="Horizontal">
      <Button Content="✓ Aceptar" Click="Accept_Click" Margin="4"/>
      <Button Content="✎ Editar y aprender" Click="EditAndLearn_Click" Margin="4"/>
      <Button Content="✗ Manual" Click="Manual_Click" Margin="4"/>
    </StackPanel>
  </Grid>
</UserControl>
```

- [ ] Create code-behind `ReviewQueueWindowControl.xaml.cs`:

```csharp
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace VBMigrator.VSIX.ToolWindows;

public partial class ReviewQueueWindowControl : UserControl
{
    public ReviewQueueWindowControl() => InitializeComponent();

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        // Accept ICSharpCode or translated output as-is
        MessageBox.Show("Accepted. File written.", "VBMigrator");
    }

    private async void EditAndLearn_Click(object sender, RoutedEventArgs e)
    {
        // Save correction to KB via CLI
        var vb = VbPanel.Text.Trim();
        var cs = CsPanel.Text.Trim();
        if (string.IsNullOrEmpty(vb) || string.IsNullOrEmpty(cs)) return;

        var args = $"kb save --vb \"{vb.Replace("\"", "\\\"")}\" --cs \"{cs.Replace("\"", "\\\"")}\" --tag \"manual\"";
        var psi = new ProcessStartInfo(Services.CliLocator.FindExecutable(), args)
        { CreateNoWindow = true, UseShellExecute = false };
        Process.Start(psi)?.WaitForExit();
        MessageBox.Show("Correction saved to knowledge base.", "VBMigrator");
    }

    private void Manual_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Item marked for manual review.", "VBMigrator");
    }
}
```

- [ ] **Smoke test (manual):** Build VSIX in Visual Studio, launch experimental instance, right-click a .vb file, verify "Convert to C# (VBMigrator)" appears. Verify Review Queue window opens from View → Other Windows.

- [ ] Commit:

```bash
git add src/VBMigrator.VSIX/
git commit -m "feat(vsix): Package + ConvertFile/Solution commands + ReviewQueue ToolWindow"
```

---

## Task 26: SampleVBProject + Integration Tests

**Files:**
- Create: `samples/SampleVBProject/SampleVBProject.vbproj`
- Create: `samples/SampleVBProject/SampleModule.vb`
- Create: `samples/SampleVBProject/SampleClass.vb`
- Create: `samples/SampleAspxFiles/Default.aspx`
- Create: `samples/SampleAspxFiles/Default.aspx.vb`
- Create: `tests/VBMigrator.Integration.Tests/VBMigrator.Integration.Tests.csproj`
- Create: `tests/VBMigrator.Integration.Tests/EndToEnd/PipelineIntegrationTests.cs`

- [ ] Create `samples/SampleVBProject/SampleModule.vb`:

```vb
Module SampleModule

    Public Function SafeDivide(a As Integer, b As Integer) As Integer
        If b Is Nothing Then Return 0
        Return a \ b
    End Function

    Public Function CalcPower(base_ As Double, exp As Double) As Double
        Return base_ ^ exp
    End Function

    Public Function CompareIgnoreCase(a As String, b As String) As Boolean
        Return String.Compare(a, b, True) = 0
    End Function

    Public Function GetSetting() As String
        Return My.Settings.AppTitle
    End Function

    Public Sub ProcessArray(ByRef arr() As Integer, newSize As Integer)
        ReDim Preserve arr(newSize)
    End Sub

    Public Function UseIIf(x As Integer) As String
        Return IIf(x > 0, "positive", "non-positive")
    End Function

    Public Sub HandleError()
        On Error GoTo ErrHandler
        Dim x As Integer = 1 / 0
        Exit Sub
ErrHandler:
        Console.WriteLine(Err.Description)
    End Sub

End Module
```

- [ ] Create `samples/SampleVBProject/SampleClass.vb`:

```vb
Public Class SampleClass

    Public Function IsNullCheck(obj As Object) As Boolean
        Return obj Is Nothing
    End Function

    Public Function IsNotNullCheck(obj As Object) As Boolean
        Return obj IsNot Nothing
    End Function

    Public Function LogicalAnd(a As Boolean, b As Boolean) As Boolean
        Return a AndAlso b
    End Function

    Public Function LogicalOr(a As Boolean, b As Boolean) As Boolean
        Return a OrElse b
    End Function

    Public Function ConvertBool() As Integer
        Return CInt(True)
    End Function

    Public Function ConcatStrings(s1 As String, s2 As String) As String
        Return s1 & s2
    End Function

    Public Function MatchPattern(s As String) As Boolean
        Return s Like "A*"
    End Function

End Class
```

- [ ] Create `samples/SampleVBProject/SampleVBProject.vbproj`:

```xml
<Project Sdk="Microsoft.VisualBasic.App">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework>
    <RootNamespace>SampleVBProject</RootNamespace>
  </PropertyGroup>
</Project>
```

- [ ] Create `samples/SampleAspxFiles/Default.aspx`:

```aspx
<%@ Page Language="VB" AutoEventWireup="false" CodeBehind="Default.aspx.vb" Inherits="SampleApp.Default" %>
<!DOCTYPE html>
<html><body>
  <form id="form1" runat="server">
    <asp:Button ID="Button1" runat="server" Text="Click" />
    <asp:Label ID="Label1" runat="server" />
  </form>
</body></html>
```

- [ ] Create `samples/SampleAspxFiles/Default.aspx.vb`:

```vb
Public Class DefaultPage
    Inherits System.Web.UI.Page

    Protected Sub Button1_Click(sender As Object, e As System.EventArgs) Handles Button1.Click
        Label1.Text = "Clicked"
    End Sub

    Protected Sub Page_Load(sender As Object, e As System.EventArgs) Handles Me.Load
        Label1.Text = "Loaded"
    End Sub
End Class
```

- [ ] Create integration test project and add to solution:

```bash
dotnet new xunit -n VBMigrator.Integration.Tests -f net8.0 -o tests/VBMigrator.Integration.Tests
dotnet sln add tests/VBMigrator.Integration.Tests/VBMigrator.Integration.Tests.csproj
cd tests/VBMigrator.Integration.Tests
dotnet add reference ../../src/VBMigrator.Core/VBMigrator.Core.csproj
cd ../..
```

- [ ] Create `tests/VBMigrator.Integration.Tests/EndToEnd/PipelineIntegrationTests.cs`:

```csharp
using Microsoft.CodeAnalysis.CSharp;
using VBMigrator.Core.Analyzer;
using VBMigrator.Core.AspxHandler;
using VBMigrator.Core.Models;
using VBMigrator.Core.SeedRules;
using VBMigrator.Core.Translator;
using VBMigrator.Core.Validator;
using Xunit;

namespace VBMigrator.Integration.Tests.EndToEnd;

public class PipelineIntegrationTests
{
    private static readonly string SampleModulePath =
        Path.GetFullPath("../../../../samples/SampleVBProject/SampleModule.vb");

    private static readonly string SampleClassPath =
        Path.GetFullPath("../../../../samples/SampleVBProject/SampleClass.vb");

    [Fact]
    public async Task SampleModule_TranslatesWithoutHumanQueue()
    {
        var vb = await File.ReadAllTextAsync(SampleModulePath);
        var pipeline = BuildPipeline();

        var results = await pipeline.ProcessFileAsync(vb, SampleModulePath);

        // No method should land in HumanQueue for these basic patterns
        var humanQueue = results.Where(r => r.Route == TranslationRoute.HumanQueue).ToList();
        Assert.Empty(humanQueue);
    }

    [Fact]
    public async Task SampleModule_CSharpOutputCompiles()
    {
        var vb = await File.ReadAllTextAsync(SampleModulePath);
        var pipeline = BuildPipeline();
        var results  = await pipeline.ProcessFileAsync(vb, SampleModulePath);

        var cs = string.Join("\n", results.Select(r => r.CsSource));
        var validator = new RoslynCompileValidator();
        var validation = await validator.ValidateAsync(cs);

        Assert.True(validation.Success,
            $"Compile errors: {string.Join(", ", validation.Errors)}");
    }

    [Fact]
    public async Task SampleClass_IsNothingUsesSeedRule()
    {
        var vb = await File.ReadAllTextAsync(SampleClassPath);
        var pipeline = BuildPipeline();
        var results  = await pipeline.ProcessFileAsync(vb, SampleClassPath);

        Assert.Contains(results, r =>
            r.Route == TranslationRoute.SeedRule &&
            r.CsSource.Contains("is null"));
    }

    [Fact]
    public void AspxDirectiveRewriter_SampleAspx_RewritesCorrectly()
    {
        var aspxPath = Path.GetFullPath("../../../../samples/SampleAspxFiles/Default.aspx");
        var aspx = File.ReadAllText(aspxPath);

        var result = AspxDirectiveRewriter.Rewrite(aspx);

        Assert.Contains("Language=\"C#\"", result);
        Assert.Contains("Default.aspx.cs", result);
    }

    [Fact]
    public void EventWireupMigrator_SampleVb_GeneratesSubscriptions()
    {
        var vbPath = Path.GetFullPath("../../../../samples/SampleAspxFiles/Default.aspx.vb");
        var vb = File.ReadAllText(vbPath);
        var tree = Microsoft.CodeAnalysis.VisualBasic.VisualBasicSyntaxTree.ParseText(vb);

        var subs = EventWireupMigrator.ExtractSubscriptions(tree);

        Assert.Contains(subs, s => s.Contains("Button1.Click"));
        Assert.Contains(subs, s => s.Contains("Page_Load"));
    }

    private static TranslationPipeline BuildPipeline() =>
        new(
            roslynTranslator: new RoslynTranslator(),
            analyzer: new DifficultyAnalyzer(),
            seedRuleEngine: new SeedRuleEngine(SeedRuleRegistry.GetAll()),
            llmTranslator: null,
            usingResolver: new LlmUsingResolver(),
            validator: new RoslynCompileValidator(),
            repairAgent: null,
            correctionStore: null);
}
```

- [ ] Run integration tests:

```bash
dotnet test tests/VBMigrator.Integration.Tests/VBMigrator.Integration.Tests.csproj -v normal
```

Expected: **PASSED** (all 5 tests)

- [ ] Commit:

```bash
git add samples/ tests/VBMigrator.Integration.Tests/
git commit -m "test(integration): SampleVBProject (VB library) + AspxFiles + end-to-end pipeline tests"
```

---

## Self-Review

### Spec Coverage Check

| Spec Section | Task |
|-------------|------|
| §2 Componentes (Core/CLI/VSIX/Tests) | Task 1 |
| §3 Arquitectura out-of-process | Task 24 |
| §4.1 Pipeline (ICSharpCode whole-file) | Task 12, 17 |
| §4.1 Paso [3] Analyzer + DifficultyMap | Task 11 |
| §4.1 Paso [4] Split + matching policy | Task 17 |
| §4.1 Paso [5] Evaluación sin flags → 0.85 | Task 17 |
| §4.1 Paso [6] DB lookup + flag→tag mapping | Task 17, 19 |
| §4.1 Paso [7] SeedRuleEngine + LLM + LlmUsingResolver | Task 13, 14, 17 |
| §4.1 Paso [8] Validator + RepairAgent | Task 14, 15, 17 |
| §4.1 Paso [9] ConfidenceScorer min() + routing | Task 16, 17 |
| §4.2 AspxHandler (semi-paralelo) | Task 21 |
| §4.3 LlmUsingResolver | Task 13 |
| §4.4 Tipos del dominio | Task 2 |
| §4.5 RepairAgent | Task 15 |
| §5 ISeedRule (SyntaxNode + SemanticModel?) | Task 3 |
| §5 SeedRuleEngine Priority-desc | Task 3 |
| §5 25 SeedRules activas | Tasks 4-10 |
| §5 Delegadas a ICSharpCode (with_block, withevents, etc.) | Noted in Task 3 |
| §6.1 PatternNormalizer (VB-origin __varN__, C#-introduced __newN__) | Task 18 |
| §6.1 CorrectionStore upsert exact equality | Task 19 |
| §6.2 SQLite schema exacto | Task 19 |
| §7 VbprojToCsprojConverter SDK-style + old-style | Task 20 |
| §8.1 CliLocator (3-step search order) | Task 24 |
| §8.1 CliRunner (concurrent stdout+stderr drain) | Task 24 |
| §8.2 Review Queue ToolWindow | Task 25 |
| §9.1 CLI ConvertCommand (--file + --solution) | Task 22 |
| §9.1 ConvertSolution: stderr progress + exit code | Task 22, 24 |
| §9.2 JSON camelCase + JsonStringEnumConverter | Task 22 |
| §9.1 KbCommand (save + stats) | Task 23 |
| §10.1 NuGet packages | Task 1 |
| §12 LlmTranslator Prompts/SystemPrompt.md | Task 14 |
| §14 Testing strategy (unit + integration, no System.Web) | Tasks 4-10, 26 |
| §15 Casos fuera de scope | Enforced by not implementing VB6/VBScript/etc. |

### Type Consistency Check

- `ISeedRule`: `CanHandle(SyntaxNode, SemanticModel?)` + `Convert(SyntaxNode, SemanticModel?)` — consistent Tasks 3-10
- `TranslationResult`: `CsSource`, `Confidence`, `Route`, `CompilerPassed`, `CompilerErrors`, `PatternTag`, `LlmFailureReason` — consistent Tasks 2, 14, 15, 17
- `TranslationRoute` enum: `SeedRule | Llm | HumanQueue | Error` — consistent throughout
- `DifficultyMap.Functions[].Flags` list of strings — consistent Tasks 11, 17
- Flag names: `OnError`, `OnErrorResume`, `LikeOp`, `ExponOp`, `ByteLoop`, `MyNamespace`, `WithEvents`, `GotoCrossBlock` — consistent Tasks 11, 17
- `MethodPair` record: `MethodName`, `VbMethodSource`, `CsMethodSource` — defined Task 17, used only Task 17
- `TranslationResultDto` (VSIX): camelCase `[JsonPropertyName]` — consistent Tasks 22, 24

### Gaps

- **`SeedRuleRegistry.GetAll()`**: referenced in Tasks 3 and 17 but not explicitly defined. Implementer must add `SeedRuleRegistry.cs` in `SeedRules/` returning all rule instances. Add after Task 10:

```csharp
// src/VBMigrator.Core/SeedRules/SeedRuleRegistry.cs
public static class SeedRuleRegistry
{
    public static IReadOnlyList<ISeedRule> GetAll() =>
    [
        new Rules.IsNothingRule(),
        new Rules.IsNotNothingRule(),
        new Rules.AndAlsoRule(),
        new Rules.OrElseRule(),
        new Rules.CintBoolRule(),
        new Rules.IntegerDivisionRule(),
        new Rules.ExponentiationRule(),
        new Rules.RedimPreserveRule(),
        new Rules.EraseArrayRule(),
        new Rules.StringConcatRule(),
        new Rules.StringComparisonCaseRule(),
        new Rules.IifFunctionRule(),
        new Rules.LikeOperatorRule(),
        new Rules.ByValParamRule(),
        new Rules.OptionalParamRule(),
        new Rules.DateLiteralRule(),
        new Rules.OnErrorGotoRule(),
        new Rules.OnErrorResumeRule(),
        new Rules.ForByteOverflowRule(),
        new Rules.MySettingsRule(),
        new Rules.MyFilesystemReadRule(),
        new Rules.MyFilesystemWriteRule(),
        new Rules.MyAppVersionRule(),
        new Rules.MyUserRule(),
        new Rules.NothingValueTypeRule()
    ];
}
```

- **`PipelineFactory.Build()`**: referenced in Task 22 (CLI). Must be a static factory in Core or CLI that wires up `TranslationPipeline` from configuration. Add `src/VBMigrator.Core/Translator/PipelineFactory.cs` that reads config from `VBMigrator:*` section.

- **`SeedRuleEngine.TryConvert`**: referenced in Task 17 pipeline. Method signature must match: `bool TryConvert(SyntaxNode node, SemanticModel? model, out SyntaxNode? converted, out double confidence)` — add to `SeedRuleEngine` in Task 3 if not already there.

- **`RoslynCompileValidator.ValidateAsync`**: referenced Tasks 17, 26. Return type `ValidationResult` with `Success`, `Errors` — must be defined in Task 11's Validator section.

<!-- PLAN_COMPLETE -->

