using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// on_error_goto: On Error GoTo Label ... Label: ... Err.Description
///   → try { } catch (Exception ex) { } with ex.Message replacing Err.Description
/// Priority 150.
/// CROSSBLOCK_GOTO: if HasGotoCrossBlock is true, CanHandle returns false → HumanQueue.
/// </summary>
public sealed class OnErrorGotoRule : ISeedRule
{
    public string Tag => "on_error_goto";
    public int Priority => 150;

    /// <summary>
    /// Set by pipeline from DifficultyMap GotoCrossBlock flag.
    /// When true, CanHandle returns false → method routes to HumanQueue.
    /// </summary>
    public bool HasGotoCrossBlock { get; set; }

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (HasGotoCrossBlock) return false;
        if (node is not OnErrorGoToStatementSyntax stmt) return false;
        // GoTo 0 clears the error handler — skip (label text == "0")
        return stmt.Label.ToString().Trim() != "0";
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var stmt = (OnErrorGoToStatementSyntax)node;
        var labelName = stmt.Label.ToString().Trim();

        var csText = $@"try
{{
    // TODO: VBMigrator — body before On Error GoTo {labelName}
}}
catch (Exception ex)
{{
    // {labelName}: (translated from VB error handler)
    // ex.Message replaces Err.Description
}}";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(csText);
    }
}
