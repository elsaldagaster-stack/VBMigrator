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
