using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// nothing_assign_valuetype: Dim x As Integer = Nothing  →  int x = default;
/// CanHandle REQUIRES semanticModel != null to confirm the declared type is a value type.
/// Without semanticModel: CanHandle returns false → the node goes to LLM.
/// </summary>
public sealed class NothingValueTypeRule : ISeedRule
{
    public string Tag => "nothing_assign_valuetype";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (semanticModel is null) return false;
        if (node is not LocalDeclarationStatementSyntax decl) return false;

        foreach (var declarator in decl.Declarators)
        {
            var init = declarator.Initializer?.Value;
            if (init is null) continue;
            if (!init.IsKind(SyntaxKind.NothingLiteralExpression)) continue;
            if (declarator.AsClause is not SimpleAsClauseSyntax asClause) continue;
            var typeInfo = semanticModel.GetTypeInfo(asClause.Type);
            if (typeInfo.Type is { IsValueType: true })
                return true;
        }
        return false;
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var decl = (LocalDeclarationStatementSyntax)node;
        var declarator = decl.Declarators.First(d =>
            d.Initializer?.Value?.IsKind(SyntaxKind.NothingLiteralExpression) == true);

        var asClause = (SimpleAsClauseSyntax)declarator.AsClause!;
        var typeName = asClause.Type.ToString().Trim();
        var varName  = declarator.Names[0].Identifier.Text;

        var csType = typeName switch
        {
            "Integer" => "int",
            "Long"    => "long",
            "Short"   => "short",
            "Byte"    => "byte",
            "Single"  => "float",
            "Double"  => "double",
            "Decimal" => "decimal",
            "Boolean" => "bool",
            "Char"    => "char",
            _         => typeName
        };

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseStatement($"{csType} {varName} = default;");
    }
}
