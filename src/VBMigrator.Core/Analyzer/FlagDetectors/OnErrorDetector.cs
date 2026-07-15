using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Analyzer.FlagDetectors;

/// <summary>
/// Detects On Error GoTo and On Error Resume Next within a method body.
/// Also detects GotoCrossBlock: a GoTo that targets a label at a different block nesting level.
/// </summary>
public static class OnErrorDetector
{
    public const string FlagOnError        = "OnError";
    public const string FlagOnErrorResume  = "OnErrorResume";
    public const string FlagGotoCrossBlock = "GotoCrossBlock";

    public static (bool HasOnError, bool HasOnErrorResume, bool HasGotoCrossBlock) Detect(SyntaxNode methodNode)
    {
        var descendants = methodNode.DescendantNodes().ToList();

        // stmt.Label.ToString().Trim() == "0" means GoTo 0 (clears handler) — not a real handler
        bool hasOnError = descendants.Any(n => n is OnErrorGoToStatementSyntax stmt
                              && stmt.Label.ToString().Trim() != "0");
        bool hasOnErrorResume = descendants.Any(n => n is OnErrorResumeNextStatementSyntax);
        bool hasGotoCrossBlock = hasOnError && DetectCrossBlockGoTo(methodNode, descendants);

        return (hasOnError, hasOnErrorResume, hasGotoCrossBlock);
    }

    private static bool DetectCrossBlockGoTo(SyntaxNode methodNode, List<SyntaxNode> descendants)
    {
        var labels = descendants.OfType<LabelStatementSyntax>()
            .Select(l => l.LabelToken.Text)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var gotoStmt in descendants.OfType<GoToStatementSyntax>())
        {
            var targetLabel = gotoStmt.Label.ToString().Trim();
            if (!labels.Contains(targetLabel)) continue;

            var gotoDepth = CountBlockDepth(gotoStmt);

            var labelNode = descendants.OfType<LabelStatementSyntax>()
                .FirstOrDefault(l => string.Equals(l.LabelToken.Text, targetLabel, StringComparison.OrdinalIgnoreCase));
            if (labelNode is null) continue;

            var labelDepth = CountBlockDepth(labelNode);
            if (gotoDepth != labelDepth) return true;
        }
        return false;
    }

    private static int CountBlockDepth(SyntaxNode node)
        => node.Ancestors().Count(n =>
            n is MultiLineIfBlockSyntax or ForBlockSyntax or WhileBlockSyntax or
                 TryBlockSyntax or SelectBlockSyntax or WithBlockSyntax or DoLoopBlockSyntax);
}
