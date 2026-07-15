namespace VBMigrator.Core.Models;

public class TranslationLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? PatternId { get; set; }
    public required string FilePath { get; set; }
    public string? MethodName { get; set; }
    public required string VbInput { get; set; }
    public required string CsOutput { get; set; }
    public bool WasCorrected { get; set; }
    public string? HumanCs { get; set; }
    public bool CompilerPassed { get; set; }
    public double Confidence { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
