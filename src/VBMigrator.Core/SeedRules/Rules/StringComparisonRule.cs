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
