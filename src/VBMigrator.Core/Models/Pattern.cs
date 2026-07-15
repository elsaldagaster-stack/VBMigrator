namespace VBMigrator.Core.Models;

public class Pattern
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Tag { get; set; }
    public required string VbTemplate { get; set; }
    public required string CsTemplate { get; set; }
    public string Source { get; set; } = "seed";
    public int Applied { get; set; }
    public int Successes { get; set; }
    public byte[]? Embedding { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public double Confidence
    {
        get
        {
            var base_ = Source switch
            {
                "seed"          => 0.90,
                "human"         => 0.70,
                _               => 0.80
            };
            if (Applied == 0) return base_;
            return Math.Min(1.0, base_ + (double)Successes / Applied * 0.30);
        }
    }
}
