using System.IO;

namespace VBMigrator.VSIX.Services;

public class ReviewQueueItem
{
    public int Id { get; set; }
    public string FilePath { get; set; } = "";
    public string VbSource { get; set; } = "";
    public string CsSource { get; set; } = "";
    public double Confidence { get; set; }
    public string? Tag { get; set; }

    public string FileName => Path.GetFileName(FilePath);
    public string DisplayName => $"{FileName}  ({Confidence:P0})  [{Tag ?? "—"}]";
}
