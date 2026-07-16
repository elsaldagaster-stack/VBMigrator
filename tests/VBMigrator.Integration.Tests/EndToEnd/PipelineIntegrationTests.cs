using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.Analyzer;
using VBMigrator.Core.AspxHandler;
using VBMigrator.Core.Models;
using VBMigrator.Core.SeedRules;
using VBMigrator.Core.Translator;
using VBMigrator.Core.Validator;
using Xunit;

namespace VBMigrator.Integration.Tests.EndToEnd;

public class PipelineIntegrationTests
{
    private static readonly string SampleModulePath =
        Path.GetFullPath("../../../../../samples/SampleVBProject/SampleModule.vb");

    private static readonly string SampleClassPath =
        Path.GetFullPath("../../../../../samples/SampleVBProject/SampleClass.vb");

    [Fact]
    public async Task SampleModule_TranslatesWithoutHumanQueue()
    {
        var vb = await File.ReadAllTextAsync(SampleModulePath);
        var pipeline = BuildPipeline();

        var results = await pipeline.ProcessFileAsync(vb, SampleModulePath);

        var humanQueue = results.Where(r => r.Route == TranslationRoute.HumanQueue).ToList();
        Assert.Empty(humanQueue);
    }

    [Fact]
    public async Task SampleModule_CSharpOutputCompiles()
    {
        var vb = await File.ReadAllTextAsync(SampleModulePath);
        // Validate the ICSharpCode whole-file output — per-method CsSource are
        // partial snippets (expressions/statements) that aren't standalone compilable units.
        var roslynTranslator = new RoslynTranslator();
        var initialResult = await roslynTranslator.ConvertFileAsync(vb, SampleModulePath);
        var validator = new RoslynCompileValidator();
        var validation = await validator.ValidateAsync(initialResult.CsSource);

        Assert.True(validation.Success,
            $"Compile errors: {string.Join(", ", validation.Errors)}");
    }

    [Fact]
    public async Task SampleClass_IsNothingUsesSeedRule()
    {
        var vb = await File.ReadAllTextAsync(SampleClassPath);
        var pipeline = BuildPipeline();
        var results = await pipeline.ProcessFileAsync(vb, SampleClassPath);

        // ICSharpCode converts `Is Nothing` to `== null` or `is null` depending on version.
        Assert.Contains(results, r =>
            r.Route == TranslationRoute.SeedRule &&
            (r.CsSource.Contains("is null") || r.CsSource.Contains("== null")));
    }

    [Fact]
    public void AspxDirectiveRewriter_SampleAspx_RewritesCorrectly()
    {
        var aspxPath = Path.GetFullPath("../../../../../samples/SampleAspxFiles/Default.aspx");
        var aspx = File.ReadAllText(aspxPath);

        var result = AspxDirectiveRewriter.Rewrite(aspx);

        Assert.Contains("Language=\"C#\"", result);
        Assert.Contains("Default.aspx.cs", result);
    }

    [Fact]
    public void EventWireupMigrator_SampleVb_GeneratesSubscriptions()
    {
        var vbPath = Path.GetFullPath("../../../../../samples/SampleAspxFiles/Default.aspx.vb");
        var vb = File.ReadAllText(vbPath);
        var tree = VisualBasicSyntaxTree.ParseText(vb);

        var subs = EventWireupMigrator.ExtractSubscriptions(tree);

        Assert.Contains(subs, s => s.Contains("Button1.Click"));
        Assert.Contains(subs, s => s.Contains("Page_Load"));
    }

    private static TranslationPipeline BuildPipeline() =>
        new(
            roslynTranslator: new RoslynTranslator(),
            analyzer: new DifficultyAnalyzer(),
            seedRuleEngine: new SeedRuleEngine(SeedRuleRegistry.GetAll()),
            llmTranslator: null,
            usingResolver: new LlmUsingResolver(),
            validator: new RoslynCompileValidator(),
            repairAgent: null,
            correctionStore: null);
}
