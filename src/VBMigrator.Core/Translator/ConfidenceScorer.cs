using VBMigrator.Core.Models;

namespace VBMigrator.Core.Translator;

public static class ConfidenceScorer
{
    // Conservative: one low-confidence node pulls down the whole method.
    public static double Score(IEnumerable<double> nodeConfidences)
        => nodeConfidences.DefaultIfEmpty(0.0).Min();

    public static TranslationRoute GetRoute(double confidence) => confidence switch
    {
        >= 0.85 => TranslationRoute.SeedRule,   // AUTO tier
        >= 0.65 => TranslationRoute.Llm,        // SUGGEST tier
        _       => TranslationRoute.HumanQueue
    };
}
