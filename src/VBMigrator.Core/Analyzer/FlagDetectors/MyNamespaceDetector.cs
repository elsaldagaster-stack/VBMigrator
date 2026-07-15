using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Analyzer.FlagDetectors;

/// <summary>
/// Detects any use of the My namespace within a method body.
/// </summary>
public static class MyNamespaceDetector
{
    public const string Flag = "MyNamespace";

    public static bool HasMyNamespace(SyntaxNode methodNode)
        => methodNode.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Any(id => string.Equals(id.Identifier.Text, "My", StringComparison.Ordinal) &&
                       id.Parent is MemberAccessExpressionSyntax);
}
