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

        // Warning comment ALWAYS emitted per spec. Inside parens so ToString() includes it (not leading trivia).
        var csText =
            $"(/* ⚠ VBMigrator: IIf evalúa ambos brazos en VB; ternario ?: no lo hace */ {cond} ? {a} : {b})";

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(csText);
    }
}
