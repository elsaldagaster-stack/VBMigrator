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
