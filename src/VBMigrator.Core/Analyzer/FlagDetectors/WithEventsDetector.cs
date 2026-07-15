using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Analyzer.FlagDetectors;

/// <summary>
/// Detects WithEvents field declarations in the class containing the method.
/// </summary>
public static class WithEventsDetector
{
    public const string Flag = "WithEvents";

    public static bool HasWithEventsInScope(SyntaxNode methodNode)
    {
        var containingType = methodNode.Ancestors()
            .FirstOrDefault(n => n is ClassBlockSyntax or ModuleBlockSyntax);
        if (containingType is null) return false;

        return containingType.DescendantNodes()
            .OfType<FieldDeclarationSyntax>()
            .Any(f => f.Modifiers.Any(m => m.IsKind(SyntaxKind.WithEventsKeyword)));
    }
}
