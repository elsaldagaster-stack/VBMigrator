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
    private static readonly string _systemPrompt = File.Exists("Translator/Prompts/SystemPrompt.md")
        ? File.ReadAllText("Translator/Prompts/SystemPrompt.md")
        : "Traduce este VB.NET snippet a C#. Devuelve solo el código, sin using statements ni explicación.";

    public async Task<TranslationResult> TranslateAsync(string vbSource, string? fewShotExample)
    {
        var userContent = fewShotExample is null
            ? $"```vb\n{vbSource}\n```"
            : $"Ejemplo:\nVB: {fewShotExample}\n\n```vb\n{vbSource}\n```";

        var parameters = new MessageParameters
        {
            Model          = "claude-sonnet-4-6",
            MaxTokens      = 2048,
            SystemMessage  = _systemPrompt,
            Messages       = [new Message(RoleType.User, userContent)]
        };

        LlmFailureReason? failureReason = null;

        for (int attempt = 0; attempt <= retryCount; attempt++)
        {
            try
            {
                var response = await client.Messages(parameters);
                var csSource = response.FirstMessage?.Text ?? string.Empty;

                return new TranslationResult
                {
                    CsSource       = csSource.Trim(),
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
}
