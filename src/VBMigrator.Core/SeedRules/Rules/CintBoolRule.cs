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
        // VB parser emits CInt(x) as PredefinedCastExpressionSyntax, not InvocationExpression
        if (node is not PredefinedCastExpressionSyntax cast) return false;
        if (!cast.Keyword.IsKind(SyntaxKind.CIntKeyword)) return false;
        return cast.Expression is LiteralExpressionSyntax lit
            && (lit.IsKind(SyntaxKind.TrueLiteralExpression) || lit.IsKind(SyntaxKind.FalseLiteralExpression));
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var cast = (PredefinedCastExpressionSyntax)node;
        var arg = cast.Expression;
        bool isTrue = arg.IsKind(SyntaxKind.TrueLiteralExpression);

        var csExpr = isTrue
            ? "/* VB: CInt(True) = -1 */ (true ? -1 : 0)"
            : "/* VB: CInt(False) = 0 */ 0";

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(csExpr);
    }
}
