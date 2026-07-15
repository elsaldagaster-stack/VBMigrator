using VBMigrator.Core.Models;
using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class ConfidenceScorerTests
{
    [Theory]
    [InlineData(new[] { 0.90, 0.70, 0.85 }, 0.70)]
    [InlineData(new[] { 0.90 }, 0.90)]
    [InlineData(new[] { 0.50, 0.50 }, 0.50)]
    public void Score_ReturnsMin(double[] confidences, double expected)
    {
        var score = ConfidenceScorer.Score(confidences);
        Assert.Equal(expected, score, precision: 10);
    }

    [Theory]
    [InlineData(0.85, TranslationRoute.SeedRule)]
    [InlineData(0.90, TranslationRoute.SeedRule)]
    [InlineData(0.84, TranslationRoute.Llm)]
    [InlineData(0.65, TranslationRoute.Llm)]
    [InlineData(0.64, TranslationRoute.HumanQueue)]
    [InlineData(0.00, TranslationRoute.HumanQueue)]
    public void Route_MapsCorrectly(double confidence, TranslationRoute expectedRoute)
    {
        var route = ConfidenceScorer.GetRoute(confidence);
        Assert.Equal(expectedRoute, route);
    }
}
