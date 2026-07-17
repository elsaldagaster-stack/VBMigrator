using Anthropic.SDK.Messaging;

namespace VBMigrator.Core.Translator;

public record RepairResult(bool Repaired, string CsSource, double Confidence);

public class RepairAgent(IAnthropicClient client)
{
    private const string RepairSystem =
        "You are a C# repair agent. Return ONLY the corrected C# code. No explanations, no markdown fences, no comments about what changed.";

    public async Task<RepairResult> RepairAsync(
        string csMethod, string errorMessage,
        IEnumerable<string> affectedLines, double originalConfidence)
    {
        var errors = string.Join("\n", affectedLines);
        var user   = $"Fix the compilation errors in this C# code.\n\nErrors:\n{errors}\n\nCode:\n{csMethod}\n\nReturn only the corrected code.";

        var parameters = new MessageParameters
        {
            Model         = "claude-sonnet-4-6",
            MaxTokens     = 2048,
            SystemMessage = RepairSystem,
            Messages      = [new Message(RoleType.User, user)]
        };

        try
        {
            var response = await client.Messages(parameters);
            var fixed_   = ExtractCode(response.FirstMessage?.Text ?? string.Empty);
            if (string.IsNullOrWhiteSpace(fixed_)) return new RepairResult(false, csMethod, 0.0);
            return new RepairResult(true, fixed_, Math.Max(0.0, originalConfidence - 0.10));
        }
        catch
        {
            return new RepairResult(false, csMethod, 0.0);
        }
    }

    private static string ExtractCode(string text)
    {
        text = text.Trim();
        if (!text.Contains("```")) return text;
        var sb = new System.Text.StringBuilder();
        bool inside = false;
        foreach (var line in text.Split('\n'))
        {
            var t = line.TrimStart();
            if (!inside && t.StartsWith("```")) { inside = true; continue; }
            if (inside  && t.StartsWith("```")) { inside = false; sb.AppendLine(); continue; }
            if (inside) sb.AppendLine(line);
        }
        var extracted = sb.ToString().Trim();
        return extracted.Length > 0 ? extracted : text;
    }
}
