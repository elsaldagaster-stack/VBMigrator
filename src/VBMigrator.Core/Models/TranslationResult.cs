namespace VBMigrator.Core.Models;

public record TranslationResult
{
    public required string CsSource { get; init; }
    public double Confidence { get; init; }
    public TranslationRoute Route { get; init; }
    public bool CompilerPassed { get; init; }
    public List<string> CompilerErrors { get; init; } = new();
    public string? PatternTag { get; init; }
    public LlmFailureReason? LlmFailureReason { get; init; }
}
