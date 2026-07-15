namespace VBMigrator.Core.Models;

public record DifficultyMap
{
    public required string FilePath { get; init; }
    public int OverallScore { get; init; }
    public List<FunctionDifficulty> Functions { get; init; } = new();
}

public record FunctionDifficulty
{
    public required string MethodName { get; init; }
    public int Score { get; init; }
    public List<string> Flags { get; init; } = new();
    public TranslationRoute Route { get; init; }
}
