using Anthropic.SDK.Messaging;

namespace VBMigrator.Core.Translator;

public record RepairResult(bool Repaired, string CsSource, double Confidence);

public class RepairAgent(IAnthropicClient client)
{
    private const string RepairSystem =
        "Eres un repair agent. Fix SOLO el error indicado. No refactorices. Devuelve solo el bloque C# corregido.";

    public async Task<RepairResult> RepairAsync(
        string csMethod, string errorMessage,
        IEnumerable<string> affectedLines, double originalConfidence)
    {
        var context = string.Join("\n", affectedLines);
        var user    = $"Error: {errorMessage}\n\nCódigo:\n{context}";

        var parameters = new MessageParameters
        {
            Model         = "claude-sonnet-4-6",
            MaxTokens     = 512,
            SystemMessage = RepairSystem,
            Messages      = [new Message(RoleType.User, user)]
        };

        try
        {
            var response = await client.Messages(parameters);
            var fixed_   = response.FirstMessage?.Text ?? string.Empty;
            return new RepairResult(true, fixed_.Trim(), Math.Max(0.0, originalConfidence - 0.10));
        }
        catch
        {
            return new RepairResult(false, csMethod, 0.0);
        }
    }
}
