using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// byval_param: ByVal x As T  →  T x
/// ByVal is the default in C# — just remove the modifier.
/// Operates on ParameterSyntax nodes.
/// </summary>
public sealed class ByValParamRule : ISeedRule
{
    public string Tag => "byval_param";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not ParameterSyntax param) return false;
        return param.Modifiers.Any(m => m.IsKind(SyntaxKind.ByValKeyword));
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var param = (ParameterSyntax)node;
        var typeName = param.AsClause?.Type?.ToString().Trim() ?? "object";
        var paramName = param.Identifier.Identifier.Text;
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseParameterList($"({typeName} {paramName})")
            .Parameters[0];
    }
}

/// <summary>
/// optional_param: Optional ByVal x As T = v  →  T x = v
/// </summary>
public sealed class OptionalParamRule : ISeedRule
{
    public string Tag => "optional_param";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not ParameterSyntax param) return false;
        return param.Modifiers.Any(m => m.IsKind(SyntaxKind.OptionalKeyword));
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var param = (ParameterSyntax)node;
        var typeName  = param.AsClause?.Type?.ToString().Trim() ?? "object";
        var paramName = param.Identifier.Identifier.Text;
        var defaultVal = param.Default?.Value?.ToString().Trim() ?? "default";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseParameterList($"({typeName} {paramName} = {defaultVal})")
            .Parameters[0];
    }
}
