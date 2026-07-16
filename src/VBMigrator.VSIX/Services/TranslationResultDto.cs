using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace VBMigrator.VSIX.Services;

// Properties camelCase to match CLI JSON output (CamelCase policy)
public class TranslationResultDto
{
    [JsonPropertyName("filePath")]          public string FilePath { get; set; } = "";
    [JsonPropertyName("csSource")]          public string CsSource { get; set; } = "";
    [JsonPropertyName("confidence")]        public double Confidence { get; set; }
    [JsonPropertyName("route")]             public string Route { get; set; } = "";   // string, not enum
    [JsonPropertyName("compilerPassed")]    public bool CompilerPassed { get; set; }
    [JsonPropertyName("compilerErrors")]    public List<string> CompilerErrors { get; set; } = new();
    [JsonPropertyName("patternTag")]        public string? PatternTag { get; set; }
    [JsonPropertyName("llmFailureReason")]  public string? LlmFailureReason { get; set; }
}
