using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// string_concat: s1 &amp; s2  →  s1 + s2
/// With semanticModel: if either operand is numeric → confidence 0.70 (SUGGEST).
/// Without semanticModel: if operand name suggests numeric (heuristic) → same downgrade.
/// The Convert method always emits s1 + s2; the caller reads the numeric flag from ConfidenceHint.
/// </summary>
public sealed class StringConcatRule : ISeedRule
{
    public string Tag => "string_concat";
    public int Priority => 100;

    /// <summary>True when the last Convert detected a potentially-numeric operand.</summary>
    public bool LastConvertHadNumericOperand { get; private set; }

    private static readonly HashSet<string> _numericHints = new(StringComparer.OrdinalIgnoreCase)
    {
        "count", "total", "sum", "amount", "price", "qty", "quantity",
        "num", "number", "index", "idx", "i", "j", "k", "n", "x", "y", "z",
        "value", "val", "result", "score", "length", "len", "size"
    };

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is BinaryExpressionSyntax bin
           && bin.IsKind(SyntaxKind.ConcatenateExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin = (BinaryExpressionSyntax)node;
        var left  = bin.Left.ToString().Trim();
        var right = bin.Right.ToString().Trim();

        LastConvertHadNumericOperand = false;

        if (semanticModel is not null)
        {
            var leftType  = semanticModel.GetTypeInfo(bin.Left).Type;
            var rightType = semanticModel.GetTypeInfo(bin.Right).Type;
            bool leftNum  = IsNumericType(leftType?.SpecialType ?? Microsoft.CodeAnalysis.SpecialType.None);
            bool rightNum = IsNumericType(rightType?.SpecialType ?? Microsoft.CodeAnalysis.SpecialType.None);
            LastConvertHadNumericOperand = leftNum || rightNum;
        }
        else
        {
            var leftId  = ExtractIdentifierName(bin.Left);
            var rightId = ExtractIdentifierName(bin.Right);
            LastConvertHadNumericOperand =
                (leftId is not null && _numericHints.Contains(leftId)) ||
                (rightId is not null && _numericHints.Contains(rightId));
        }

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression($"{left} + {right}");
    }

    private static bool IsNumericType(Microsoft.CodeAnalysis.SpecialType st) => st is
        Microsoft.CodeAnalysis.SpecialType.System_Byte or
        Microsoft.CodeAnalysis.SpecialType.System_SByte or
        Microsoft.CodeAnalysis.SpecialType.System_Int16 or
        Microsoft.CodeAnalysis.SpecialType.System_UInt16 or
        Microsoft.CodeAnalysis.SpecialType.System_Int32 or
        Microsoft.CodeAnalysis.SpecialType.System_UInt32 or
        Microsoft.CodeAnalysis.SpecialType.System_Int64 or
        Microsoft.CodeAnalysis.SpecialType.System_UInt64 or
        Microsoft.CodeAnalysis.SpecialType.System_Single or
        Microsoft.CodeAnalysis.SpecialType.System_Double or
        Microsoft.CodeAnalysis.SpecialType.System_Decimal;

    private static string? ExtractIdentifierName(SyntaxNode node)
    {
        if (node is IdentifierNameSyntax id) return id.Identifier.Text;
        if (node is MemberAccessExpressionSyntax mem) return mem.Name.Identifier.Text;
        return null;
    }
}
