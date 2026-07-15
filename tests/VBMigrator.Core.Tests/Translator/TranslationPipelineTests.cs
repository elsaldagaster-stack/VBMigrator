using VBMigrator.Core.Analyzer;
using VBMigrator.Core.Models;
using VBMigrator.Core.SeedRules;
using VBMigrator.Core.Translator;
using VBMigrator.Core.Validator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class TranslationPipelineTests
{
    [Fact]
    public async Task ProcessFileAsync_SimpleMethod_UsesICSharpCodeForCleanMethods()
    {
        const string vbSource = """
            Public Class MyClass
                Public Function Add(a As Integer, b As Integer) As Integer
                    Return a + b
                End Function
            End Class
            """;

        var pipeline = BuildPipeline();
        var results  = await pipeline.ProcessFileAsync(vbSource, "Test.vb");

        Assert.All(results, r => Assert.True(r.Confidence >= 0.85));
    }

    [Fact]
    public async Task ProcessFileAsync_IsNothingPattern_UsesSeedRule()
    {
        const string vbSource = """
            Public Class MyClass
                Public Sub Check(x As Object)
                    If x Is Nothing Then
                        Return
                    End If
                End Sub
            End Class
            """;

        var pipeline = BuildPipeline();
        var results  = await pipeline.ProcessFileAsync(vbSource, "Test.vb");

        Assert.Contains(results, r => r.Route == TranslationRoute.SeedRule);
    }

    private static TranslationPipeline BuildPipeline()
    {
        var engine   = new SeedRuleEngine(SeedRuleRegistry.GetAll());
        var resolver = new LlmUsingResolver();
        var validator = new RoslynCompileValidator();
        return new TranslationPipeline(
            roslynTranslator: new RoslynTranslator(),
            analyzer:         new DifficultyAnalyzer(),
            seedRuleEngine:   engine,
            llmTranslator:    null,
            usingResolver:    resolver,
            validator:        validator,
            repairAgent:      null,
            correctionStore:  null);
    }
}
