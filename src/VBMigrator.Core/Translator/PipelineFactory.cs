using Anthropic.SDK;
using VBMigrator.Core.Analyzer;
using VBMigrator.Core.SeedRules;
using VBMigrator.Core.Validator;

namespace VBMigrator.Core.Translator;

public static class PipelineFactory
{
    public static TranslationPipeline Build()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");

        LlmTranslator? llmTranslator = null;
        RepairAgent?   repairAgent   = null;

        if (!string.IsNullOrEmpty(apiKey))
        {
            var adapter = new AnthropicClientAdapter(new AnthropicClient(apiKey));
            llmTranslator = new LlmTranslator(adapter, apiKey);
            repairAgent   = new RepairAgent(adapter);
        }

        return new TranslationPipeline(
            roslynTranslator: new RoslynTranslator(),
            analyzer:         new DifficultyAnalyzer(),
            seedRuleEngine:   new SeedRuleEngine(SeedRuleRegistry.GetAll()),
            llmTranslator:    llmTranslator,
            usingResolver:    new LlmUsingResolver(),
            validator:        new RoslynCompileValidator(),
            repairAgent:      repairAgent,
            correctionStore:  null);
    }
}
