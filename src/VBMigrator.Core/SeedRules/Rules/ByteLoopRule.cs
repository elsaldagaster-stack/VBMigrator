using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// for_byte_overflow: For i As Byte = 0 To 255
///   → HumanQueue + flag BYTE_LOOP_OVERFLOW
/// MVP: detects literal "255" only. Const-bound case not handled.
/// Comments inside block so ToString() includes them (not leading trivia).
/// </summary>
public sealed class ByteLoopRule : ISeedRule
{
    public string Tag => "for_byte_overflow";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not ForStatementSyntax forStmt) return false;

        bool isByteVar = false;
        if (forStmt.ControlVariable is VariableDeclaratorSyntax decl)
        {
            var asClause = decl.AsClause as SimpleAsClauseSyntax;
            isByteVar = string.Equals(
                asClause?.Type?.ToString().Trim(), "Byte", StringComparison.OrdinalIgnoreCase);
        }
        if (!isByteVar) return false;

        var toText = forStmt.ToValue?.ToString().Trim() ?? "";
        return toText == "255";
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        // Comments inside block so ToString() includes them (leading trivia of } is visible).
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(
            "{ /* ⚠ VBMigrator: byte loop puede ser bucle infinito en VB */ /* HUMAN_QUEUE:BYTE_LOOP_OVERFLOW */ }");
    }
}
