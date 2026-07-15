namespace VBMigrator.Core.Models;

public record TranslationRequest
{
    public required string VbSource { get; init; }
    public required string FilePath { get; init; }
    public string? MethodName { get; init; }
}
