using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace VBMigrator.VSIX.Options;

public class VBMigratorOptions : DialogPage
{
    [Category("LLM")]
    [DisplayName("Anthropic API Key")]
    [Description("API key for Claude LLM (claude-sonnet-4-6). Leave empty to use ANTHROPIC_API_KEY env var.")]
    [PasswordPropertyText(true)]
    public string AnthropicApiKey { get; set; } = "";

    public string? EffectiveApiKey =>
        !string.IsNullOrWhiteSpace(AnthropicApiKey) ? AnthropicApiKey
        : System.Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
}
