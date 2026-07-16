using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Applies seed rule conversions onto an ICSharpCode C# method output.
/// ICSharpCode produces VB compatibility shims (e.g. LikeOperator.LikeString) for
/// constructs that seed rules know how to convert to idiomatic C#. This rewriter
/// finds those shims and replaces them with the seed rules' correct C# equivalents.
/// </summary>
internal sealed class SeedRuleCsRewriter : CSharpSyntaxRewriter
{
    // One queue per tag — dequeued in document order to handle multiple occurrences.
    private readonly Queue<string> _likeQueue = new();

    public SeedRuleCsRewriter(
        IReadOnlyList<(string Tag, SyntaxNode Original, SyntaxNode Converted)> matches)
    {
        foreach (var (tag, _, converted) in matches)
        {
            if (tag == "like_operator")
                _likeQueue.Enqueue(converted.ToFullString());
        }
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        // ICSharpCode converts VB `s Like "pattern"` to LikeOperator.LikeString(s, pattern, method)
        if (_likeQueue.Count > 0 && IsLikeStringCall(node))
        {
            return SyntaxFactory.ParseExpression(_likeQueue.Dequeue())
                .WithTriviaFrom(node);
        }

        return base.VisitInvocationExpression(node);
    }

    private static bool IsLikeStringCall(InvocationExpressionSyntax node)
        => node.Expression is MemberAccessExpressionSyntax ma
           && ma.Name.Identifier.Text == "LikeString";
}
