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
