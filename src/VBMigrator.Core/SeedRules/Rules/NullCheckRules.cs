using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VBSyntaxKind = Microsoft.CodeAnalysis.VisualBasic.SyntaxKind;

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
           && bin.IsKind(VBSyntaxKind.IsExpression)
           && bin.Right is LiteralExpressionSyntax lit
           && lit.IsKind(VBSyntaxKind.NothingLiteralExpression);

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
           && bin.IsKind(VBSyntaxKind.IsNotExpression)
           && bin.Right is LiteralExpressionSyntax lit
           && lit.IsKind(VBSyntaxKind.NothingLiteralExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin = (BinaryExpressionSyntax)node;
        var leftText = bin.Left.ToString().Trim();
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression($"{leftText} is not null");
    }
}
