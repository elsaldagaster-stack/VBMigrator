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
