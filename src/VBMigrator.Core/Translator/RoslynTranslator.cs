using ICSharpCode.CodeConverter;
using ICSharpCode.CodeConverter.Common;
using Microsoft.CodeAnalysis;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Wrapper around ICSharpCode.CodeConverter.ConvertAsync.
/// Handles whole-file VB → initial C# conversion (pipeline step [2]).
/// </summary>
public class RoslynTranslator
{
    public async Task<RoslynTranslationResult> ConvertFileAsync(
        string vbSource,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(vbSource))
            return new RoslynTranslationResult { Success = false, CsSource = string.Empty };

        try
        {
            var options = new CodeWithOptions(vbSource)
                .SetFromLanguage(Microsoft.CodeAnalysis.LanguageNames.VisualBasic)
                .SetToLanguage(Microsoft.CodeAnalysis.LanguageNames.CSharp)
                .WithTypeReferences(GetDefaultReferences());

            var result = await CodeConverter.ConvertAsync(options, cancellationToken);

            return new RoslynTranslationResult
            {
                Success  = result.Success && !string.IsNullOrEmpty(result.ConvertedCode),
                CsSource = result.ConvertedCode ?? string.Empty,
                Errors   = result.Exceptions.ToList()
            };
        }
        catch (Exception ex)
        {
            return new RoslynTranslationResult
            {
                Success  = false,
                CsSource = string.Empty,
                Errors   = new List<string> { ex.Message }
            };
        }
    }

    private static IReadOnlyCollection<PortableExecutableReference> GetDefaultReferences()
    {
        var trusted = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        return new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(Path.Combine(trusted, "System.Runtime.dll")),
            MetadataReference.CreateFromFile(Path.Combine(trusted, "System.Collections.dll")),
        };
    }
}

public record RoslynTranslationResult
{
    public bool Success { get; init; }
    public required string CsSource { get; init; }
    public List<string> Errors { get; init; } = new();
}
