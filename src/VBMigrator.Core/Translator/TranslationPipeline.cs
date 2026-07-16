using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VBMigrator.Core.Analyzer;
using VBMigrator.Core.Learning;
using VBMigrator.Core.Models;
using VBMigrator.Core.SeedRules;
using VBMigrator.Core.Validator;

namespace VBMigrator.Core.Translator;

public class TranslationPipeline(
    RoslynTranslator roslynTranslator,
    DifficultyAnalyzer analyzer,
    SeedRuleEngine seedRuleEngine,
    LlmTranslator? llmTranslator,
    LlmUsingResolver usingResolver,
    RoslynCompileValidator validator,
    RepairAgent? repairAgent,
    CorrectionStore? correctionStore)
{
    // Flag → SeedRule tag mapping (§4.1 paso [6])
    private static readonly Dictionary<string, string> _flagToTag = new(StringComparer.OrdinalIgnoreCase)
    {
        ["OnError"]       = "on_error_goto",
        ["OnErrorResume"] = "on_error_resume",
        ["LikeOp"]        = "like_operator",
        ["ExponOp"]       = "exponentiation",
        ["ByteLoop"]      = "for_byte_overflow",
        ["MyNamespace"]   = "my_settings",
        ["IifOp"]         = "iif_function"
    };

    public async Task<IReadOnlyList<TranslationResult>> ProcessFileAsync(string vbSource, string filePath)
    {
        // Step [2]: ICSharpCode whole-file conversion
        var initialResult = await roslynTranslator.ConvertFileAsync(vbSource, filePath);
        var initialCs     = initialResult.CsSource;

        // Step [3]: Analyze original VB for difficulty flags
        var vbTree  = VisualBasicSyntaxTree.ParseText(vbSource);
        var diffMap = analyzer.Analyze(vbTree, filePath);

        // Step [4]: Split and pair methods
        var pairs = PairMethods(vbSource, initialCs, diffMap).ToList();

        var results = new List<TranslationResult>();
        foreach (var pair in pairs)
            results.Add(await ProcessMethodPairAsync(pair, diffMap));

        return results;
    }

    private async Task<TranslationResult> ProcessMethodPairAsync(MethodPair pair, DifficultyMap diffMap)
    {
        // Step [5]: No flags → trust ICSharpCode output
        var funcDiff = diffMap.Functions.FirstOrDefault(f => f.MethodName == pair.MethodName);
        if (funcDiff == null || funcDiff.Flags.Count == 0)
        {
            return new TranslationResult
            {
                CsSource       = pair.CsMethodSource,
                Confidence     = 0.85,
                Route          = TranslationRoute.SeedRule,
                CompilerPassed = true
            };
        }

        // Step [6]: DB lookup via flag→tag mapping
        string? fewShot = null;
        if (correctionStore != null)
        {
            var primaryFlag = funcDiff.Flags.First();
            if (_flagToTag.TryGetValue(primaryFlag, out var tag))
                fewShot = await correctionStore.GetFewShotAsync(tag);
        }

        // Step [7a]: SeedRuleEngine — apply rules on VB method, embed results into
        //            ICSharpCode C# output via SeedRuleCsRewriter (preserves method wrapper)
        var methodVbRoot    = VisualBasicSyntaxTree.ParseText(pair.VbMethodSource).GetRoot();
        var seedMatches     = seedRuleEngine.Apply(methodVbRoot);
        var nodeConfidences = new List<double>();
        string methodCs     = pair.CsMethodSource;

        if (seedMatches.Count > 0)
        {
            // Get confidence for each match (reuses TryConvert confidence logic)
            foreach (var (_, original, _) in seedMatches)
            {
                seedRuleEngine.TryConvert(original, null, out _, out var conf);
                nodeConfidences.Add(conf);
            }

            // Replace ICSharpCode VB-compat shims with seed rule outputs
            var csRoot   = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(methodCs).GetRoot();
            var rewriter = new SeedRuleCsRewriter(seedMatches);
            methodCs = rewriter.Visit(csRoot).ToFullString();
        }

        // Step [7b]: LLM when no seed rule matched
        if (seedMatches.Count == 0 && llmTranslator != null)
        {
            var llmResult = await llmTranslator.TranslateAsync(pair.VbMethodSource, fewShot);
            if (llmResult.Route == TranslationRoute.HumanQueue)
                return llmResult;
            methodCs = llmResult.CsSource;
            nodeConfidences.Add(llmResult.Confidence);
        }

        // Step [7c]: LlmUsingResolver on complete method C#
        usingResolver.Resolve(methodCs, null);

        // Step [8]: Validate
        var finalCs    = methodCs;
        var validation = await validator.ValidateAsync(finalCs);

        if (!validation.Success && repairAgent != null)
        {
            var repaired = await repairAgent.RepairAsync(
                finalCs,
                string.Join("; ", validation.Errors),
                validation.Errors,
                nodeConfidences.DefaultIfEmpty(0.75).Min());

            if (!repaired.Repaired)
                return new TranslationResult
                {
                    CsSource       = finalCs,
                    Confidence     = 0.0,
                    Route          = TranslationRoute.HumanQueue,
                    CompilerPassed = false
                };

            finalCs = repaired.CsSource;
            nodeConfidences.Add(repaired.Confidence);
        }

        // Step [9]: Score
        var finalConfidence = ConfidenceScorer.Score(nodeConfidences);
        var route           = ConfidenceScorer.GetRoute(finalConfidence);

        return new TranslationResult
        {
            CsSource       = finalCs,
            Confidence     = finalConfidence,
            Route          = route,
            CompilerPassed = validation.Success
        };
    }

    private static IEnumerable<MethodPair> PairMethods(
        string vbSource, string csSource, DifficultyMap diffMap)
    {
        var vbTree = VisualBasicSyntaxTree.ParseText(vbSource);
        var csTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(csSource);

        var vbMethods = vbTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodBlockSyntax>()
            .Select(m => (Name: m.SubOrFunctionStatement.Identifier.Text, Source: m.ToFullString()))
            .ToList();

        var csMethods = csTree.GetRoot()
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Select(m => (Name: m.Identifier.Text, Source: m.ToFullString()))
            .ToList();

        var csCtors = csTree.GetRoot()
            .DescendantNodes()
            .OfType<ConstructorDeclarationSyntax>()
            .Select(c => (Name: "New", Source: c.ToFullString()))
            .ToList();

        foreach (var vb in vbMethods)
        {
            var cs = csMethods.FirstOrDefault(c =>
                string.Equals(c.Name, vb.Name, StringComparison.OrdinalIgnoreCase));

            if (cs == default && vb.Name == "New")
                cs = csCtors.FirstOrDefault();

            yield return new MethodPair(
                MethodName:      vb.Name,
                VbMethodSource:  vb.Source,
                CsMethodSource:  cs == default ? string.Empty : cs.Source);
        }
    }
}

public record MethodPair(string MethodName, string VbMethodSource, string CsMethodSource);
