namespace VBMigrator.Core.Learning;

/// <summary>
/// Stores and retrieves human-corrected VB→C# pattern pairs from SQLite.
/// Minimal stub — full implementation in Task 18 (PatternRepository).
/// </summary>
public class CorrectionStore
{
    public virtual Task<string?> GetFewShotAsync(string tag)
        => Task.FromResult<string?>(null);

    public virtual Task StoreAsync(string tag, string vbTemplate, string csTemplate)
        => Task.CompletedTask;
}
