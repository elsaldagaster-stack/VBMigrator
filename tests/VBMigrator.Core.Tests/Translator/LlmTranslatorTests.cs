using Anthropic.SDK.Messaging;
using System.Net;
using VBMigrator.Core.Models;
using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class LlmTranslatorTests
{
    [Fact]
    public async Task TranslateAsync_ReturnsLlmRoute_WhenSuccessful()
    {
        var fakeClient = new FakeAnthropicClient("int x = 0;");
        var translator = new LlmTranslator(fakeClient, null);

        var result = await translator.TranslateAsync("Dim x As Integer = 0", null);

        Assert.Equal(TranslationRoute.Llm, result.Route);
        Assert.Contains("int x", result.CsSource);
        Assert.True(result.Confidence > 0);
    }

    [Fact]
    public async Task TranslateAsync_ReturnsHumanQueue_AfterMaxRetries()
    {
        var fakeClient = new FakeAnthropicClient(null, throwRateLimit: true);
        var translator = new LlmTranslator(fakeClient, null, retryCount: 2, retryBaseDelayMs: 1);

        var result = await translator.TranslateAsync("Dim x As Integer = 0", null);

        Assert.Equal(TranslationRoute.HumanQueue, result.Route);
        Assert.Equal(LlmFailureReason.RateLimit, result.LlmFailureReason);
    }
}

public class FakeAnthropicClient : IAnthropicClient
{
    private readonly string? _response;
    private readonly bool _throwRateLimit;

    public FakeAnthropicClient(string? response, bool throwRateLimit = false)
    {
        _response = response;
        _throwRateLimit = throwRateLimit;
    }

    public Task<MessageResponse> Messages(MessageParameters parameters, CancellationToken cancellationToken = default)
    {
        if (_throwRateLimit)
            throw new HttpRequestException("Rate limit exceeded", null, HttpStatusCode.TooManyRequests);

        var msg = new MessageResponse
        {
            Content    = [new TextContent { Text = _response! }],
            StopReason = "end_turn"
        };
        return Task.FromResult(msg);
    }
}
