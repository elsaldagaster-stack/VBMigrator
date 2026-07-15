using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Analyzer.FlagDetectors;

/// <summary>
/// Detects VB-specific operators: Like, ^ exponentiation, Byte loop overflow.
/// </summary>
public static class OperatorDetector
{
    public const string FlagLikeOp   = "LikeOp";
    public const string FlagExponOp  = "ExponOp";
    public const string FlagByteLoop = "ByteLoop";

    public static (bool LikeOp, bool ExponOp, bool ByteLoop) Detect(SyntaxNode methodNode)
    {
        var nodes = methodNode.DescendantNodes().ToList();

        bool likeOp  = nodes.Any(n => n.IsKind(SyntaxKind.LikeExpression));
        bool exponOp = nodes.Any(n => n.IsKind(SyntaxKind.ExponentiateExpression));
        bool byteLoop = nodes.OfType<ForStatementSyntax>().Any(f =>
        {
            bool isByte = f.ControlVariable is VariableDeclaratorSyntax d &&
                          d.AsClause is SimpleAsClauseSyntax a &&
                          string.Equals(a.Type?.ToString().Trim(), "Byte", StringComparison.OrdinalIgnoreCase);
            return isByte && f.ToValue?.ToString().Trim() == "255";
        });

        return (likeOp, exponOp, byteLoop);
    }
}
