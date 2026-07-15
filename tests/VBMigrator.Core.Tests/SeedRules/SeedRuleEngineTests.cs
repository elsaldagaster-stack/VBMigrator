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
