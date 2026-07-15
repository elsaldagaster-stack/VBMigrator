using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// on_error_resume: On Error Resume Next
///   → HumanQueue + comment marker
/// Priority 150. No per-statement wrapping in MVP.
/// Comments placed inside block so ToString() includes them (not leading trivia).
/// </summary>
public sealed class OnErrorResumeRule : ISeedRule
{
    public string Tag => "on_error_resume";
    public int Priority => 150;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is OnErrorResumeNextStatementSyntax;

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        // Comments inside block so ToString() includes them (leading trivia of } is visible).
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(
            "{ /* ⚠ VBMigrator: On Error Resume Next — requiere revisión manual */ /* HUMAN_QUEUE:on_error_resume */ }");
    }
}
