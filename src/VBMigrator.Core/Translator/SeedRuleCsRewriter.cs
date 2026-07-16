using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Applies seed rule conversions onto an ICSharpCode C# method output.
/// ICSharpCode produces VB compatibility shims for constructs that seed rules know
/// how to convert to idiomatic C#. This rewriter finds those shims and replaces them
/// with the seed rules' correct C# equivalents.
///
/// Supported tags:
///   like_operator  — LikeOperator.LikeString(...) → Regex.IsMatch(...)
///   my_settings    — My.Settings.Foo              → Properties.Settings.Default.Foo
///   my_*           — other My.X.Y patterns passed through from ICSharpCode as-is
/// </summary>
internal sealed class SeedRuleCsRewriter : CSharpSyntaxRewriter
{
    // One queue per tag — dequeued in document order to handle multiple occurrences.
    private readonly Queue<string> _likeQueue      = new();
    private readonly Queue<string> _mySettingsQueue = new();

    public SeedRuleCsRewriter(
        IReadOnlyList<(string Tag, SyntaxNode Original, SyntaxNode Converted)> matches)
    {
        foreach (var (tag, _, converted) in matches)
        {
            switch (tag)
            {
                case "like_operator":
                    _likeQueue.Enqueue(converted.ToFullString());
                    break;
                case "my_settings":
                    _mySettingsQueue.Enqueue(converted.ToFullString());
                    break;
            }
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

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        // ICSharpCode passes My.Settings.Foo through unchanged (invalid in C#).
        // Match the outermost My.Settings.<prop> access (expression = "My.Settings").
        if (_mySettingsQueue.Count > 0 && IsMySettingsAccess(node))
        {
            return SyntaxFactory.ParseExpression(_mySettingsQueue.Dequeue())
                .WithTriviaFrom(node);
        }

        return base.VisitMemberAccessExpression(node);
    }

    private static bool IsLikeStringCall(InvocationExpressionSyntax node)
        => node.Expression is MemberAccessExpressionSyntax ma
           && ma.Name.Identifier.Text == "LikeString";

    private static bool IsMySettingsAccess(MemberAccessExpressionSyntax node)
        // Expression text is "My.Settings" — the outer .Foo is node.Name
        => node.Expression.ToString() == "My.Settings";
}
