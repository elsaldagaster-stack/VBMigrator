using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.Analyzer.FlagDetectors;

/// <summary>
/// Detects late binding: calls on Object-typed variables.
/// Requires SemanticModel. Without it, returns false (conservative).
/// </summary>
public static class LateBindingDetector
{
    public const string Flag = "LateBinding";

    public static bool HasLateBinding(SyntaxNode methodNode, SemanticModel? semanticModel)
    {
        if (semanticModel is null) return false;

        foreach (var invocation in methodNode.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.Expression is MemberAccessExpressionSyntax mem)
            {
                var receiverType = semanticModel.GetTypeInfo(mem.Expression).Type;
                if (receiverType is not null &&
                    receiverType.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Object)
                    return true;
            }
        }
        return false;
    }
}
