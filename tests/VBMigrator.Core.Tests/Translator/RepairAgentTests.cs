using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class RepairAgentTests
{
    [Fact]
    public async Task RepairAsync_ReducesConfidenceByTen_WhenSuccessful()
    {
        var fake  = new FakeAnthropicClient("int x = 0; // fixed");
        var agent = new RepairAgent(fake);

        var result = await agent.RepairAsync(
            csMethod:           "int x = foo();",
            errorMessage:       "CS0103: 'foo' does not exist",
            affectedLines:      ["int x = foo();"],
            originalConfidence: 0.90);

        Assert.True(result.Repaired);
        Assert.Equal(0.80, result.Confidence, precision: 2);
        Assert.Contains("fixed", result.CsSource);
    }

    [Fact]
    public async Task RepairAsync_ReturnsZeroConfidence_WhenLlmFails()
    {
        var fake  = new FakeAnthropicClient(null, throwRateLimit: true);
        var agent = new RepairAgent(fake);

        var result = await agent.RepairAsync("bad code", "CS0001", [], 0.90);

        Assert.False(result.Repaired);
        Assert.Equal(0.0, result.Confidence);
    }
}
