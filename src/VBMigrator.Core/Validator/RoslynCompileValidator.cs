using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace VBMigrator.Core.Validator;

/// <summary>
/// Validates C# source by parsing and checking for syntax/semantic errors.
/// MVP: parse-level diagnostics only. Full compilation added when references are available.
/// </summary>
public class RoslynCompileValidator
{
    public Task<ValidationResult> ValidateAsync(string csSource)
    {
        if (string.IsNullOrWhiteSpace(csSource))
            return Task.FromResult(new ValidationResult(false, ["Empty source"]));

        var tree = CSharpSyntaxTree.ParseText(csSource);
        var errors = tree.GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString())
            .ToList();

        return Task.FromResult(new ValidationResult(errors.Count == 0, errors));
    }
}
