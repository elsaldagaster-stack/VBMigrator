using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using System.Net;
using VBMigrator.Core.Models;

namespace VBMigrator.Core.Translator;

public interface IAnthropicClient
{
    Task<MessageResponse> Messages(MessageParameters parameters, CancellationToken cancellationToken = default);
}

public class AnthropicClientAdapter(AnthropicClient inner) : IAnthropicClient
{
    public Task<MessageResponse> Messages(MessageParameters p, CancellationToken ct = default)
        => inner.Messages.GetClaudeMessageAsync(p, null, ct);
}

public class LlmTranslator(IAnthropicClient client, string? apiKey,
    int retryCount = 2, int retryBaseDelayMs = 1000)
{
    private const string SystemPrompt =
        "You are a VB.NET to C# migration specialist. " +
        "Return ONLY the corrected C# method body. " +
        "No class declaration, no using statements, no explanations, no markdown fences.";

    /// <summary>
    /// Targeted surgical fix: LLM receives the specific flag that triggered it,
    /// the ICSharpCode baseline attempt, and the original VB method.
    /// This makes the LLM a fixer, not a blind re-translator.
    /// </summary>
    public async Task<TranslationResult> TranslateAsync(
        string vbMethod,
        string? csBaseline,
        string? flagHint,
        string? fewShotExample = null)
    {
        var sb = new System.Text.StringBuilder();

        if (flagHint != null)
            sb.AppendLine($"PATTERN TO FIX: {flagHint}");

        if (fewShotExample != null)
            sb.AppendLine($"EXAMPLE:\n{fewShotExample}\n");

        sb.AppendLine("VB.NET original:");
        sb.AppendLine("```vb");
        sb.AppendLine(vbMethod);
        sb.AppendLine("```");

        if (!string.IsNullOrWhiteSpace(csBaseline))
        {
            sb.AppendLine();
            sb.AppendLine("ICSharpCode translation (fix this, do not re-translate from scratch):");
            sb.AppendLine("```cs");
            sb.AppendLine(csBaseline);
            sb.AppendLine("```");
        }

        sb.AppendLine();
        sb.AppendLine("Return ONLY the corrected C# method. No class wrapper, no using statements.");

        var parameters = new MessageParameters
        {
            Model         = "claude-sonnet-4-6",
            MaxTokens     = 2048,
            SystemMessage = SystemPrompt,
            Messages      = [new Message(RoleType.User, sb.ToString())]
        };

        LlmFailureReason? failureReason = null;

        for (int attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                var response = await client.Messages(parameters);
                var csSource = ExtractCode(response.FirstMessage?.Text ?? string.Empty);

                return new TranslationResult
                {
                    CsSource       = csSource,
                    Confidence     = 0.75,
                    Route          = TranslationRoute.Llm,
                    CompilerPassed = false
                };
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                failureReason = LlmFailureReason.RateLimit;
                if (attempt < retryCount)
                    await Task.Delay(retryBaseDelayMs * (int)Math.Pow(2, attempt));
            }
            catch (TaskCanceledException)
            {
                failureReason = LlmFailureReason.Timeout;
                break;
            }
            catch
            {
                failureReason = LlmFailureReason.ApiError;
                break;
            }
        }

        return new TranslationResult
        {
            CsSource         = string.Empty,
            Confidence       = 0.0,
            Route            = TranslationRoute.HumanQueue,
            CompilerPassed   = false,
            LlmFailureReason = failureReason
        };
    }

    private static string ExtractCode(string text)
    {
        text = text.Trim();
        if (!text.Contains("```")) return text;

        var parts  = new System.Text.StringBuilder();
        bool inside = false;
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (!inside && trimmed.StartsWith("```")) { inside = true; continue; }
            if (inside  && trimmed.StartsWith("```")) { inside = false; parts.AppendLine(); continue; }
            if (inside) parts.AppendLine(line);
        }

        var extracted = parts.ToString().Trim();
        return extracted.Length > 0 ? extracted : text;
    }
}
