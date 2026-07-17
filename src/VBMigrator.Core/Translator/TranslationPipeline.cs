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

        if (pairs.Count == 0)
            return [new TranslationResult { CsSource = initialCs, Confidence = 0.85, Route = TranslationRoute.SeedRule, CompilerPassed = true }];

        // Steps [5]-[7]: translate each method (no per-method validation)
        var methodResults = new List<TranslationResult>();
        foreach (var pair in pairs)
            methodResults.Add(await ProcessMethodPairAsync(pair, diffMap));

        // Reassemble method results inside the class wrapper from initialCs
        var (assembledCs, baseConfidence, baseRoute) = ReassembleFile(initialCs, methodResults);

        // Step [8]: validate the assembled file (not isolated method bodies)
        var validation = await validator.ValidateAsync(assembledCs);
        var finalCs    = assembledCs;

        if (!validation.Success && repairAgent != null)
        {
            var repaired = await repairAgent.RepairAsync(
                finalCs,
                string.Join("; ", validation.Errors),
                validation.Errors,
                baseConfidence);

            if (repaired.Repaired)
            {
                finalCs       = repaired.CsSource;
                baseConfidence = Math.Max(0.0, baseConfidence - 0.10);
            }
        }

        // Step [9]: score
        var finalRoute = !validation.Success && baseRoute != TranslationRoute.HumanQueue
            ? TranslationRoute.HumanQueue
            : baseRoute;
        var finalConfidence = validation.Success ? baseConfidence : Math.Min(baseConfidence, 0.60);

        return [new TranslationResult
        {
            CsSource       = finalCs,
            Confidence     = finalConfidence,
            Route          = finalRoute,
            CompilerPassed = validation.Success
        }];
    }

    private static (string assembledCs, double confidence, TranslationRoute route) ReassembleFile(
        string initialCs, List<TranslationResult> methodResults)
    {
        var csTree   = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(initialCs);
        var root     = csTree.GetRoot();
        var typeDecl = root.DescendantNodes()
            .OfType<Microsoft.CodeAnalysis.CSharp.Syntax.TypeDeclarationSyntax>()
            .FirstOrDefault();

        // Collect extra namespaces from resolver results not already in initialCs
        var extraUsings = methodResults
            .SelectMany(r => r.ResolvedNamespaces)
            .Distinct(StringComparer.Ordinal)
            .Where(ns => !initialCs.Contains($"using {ns};"))
            .OrderBy(ns => ns)
            .Select(ns => $"using {ns};")
            .ToList();

        string assembled;
        if (typeDecl != null)
        {
            // Header: usings + class declaration up to and including '{'
            var header = initialCs[..typeDecl.OpenBraceToken.Span.End];
            var sb = new System.Text.StringBuilder();
            // Prepend extra using directives not in ICSharpCode output
            foreach (var u in extraUsings)
                sb.AppendLine(u);
            sb.AppendLine(header);

            // Preserve non-method members (properties, fields, constants) from initialCs
            foreach (var member in typeDecl.Members)
            {
                if (member is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax
                           or Microsoft.CodeAnalysis.CSharp.Syntax.ConstructorDeclarationSyntax)
                    continue;
                foreach (var line in member.ToFullString().Trim().Split('\n'))
                    sb.AppendLine("    " + line.TrimEnd('\r'));
                sb.AppendLine();
            }

            // Append processed method results
            foreach (var r in methodResults)
            {
                foreach (var line in r.CsSource.Trim().Split('\n'))
                    sb.AppendLine("    " + line.TrimEnd('\r'));
                sb.AppendLine();
            }
            sb.Append('}');
            assembled = sb.ToString();
        }
        else
        {
            assembled = string.Join("\n\n", methodResults.Select(r => r.CsSource));
        }

        var minConfidence = methodResults.Min(r => r.Confidence);
        var worstRoute    = methodResults.Any(r => r.Route == TranslationRoute.HumanQueue)
            ? TranslationRoute.HumanQueue
            : methodResults.Any(r => r.Route == TranslationRoute.Llm)
                ? TranslationRoute.Llm
                : TranslationRoute.SeedRule;

        return (assembled, minConfidence, worstRoute);
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
        var primaryFlag = funcDiff.Flags.First();
        _flagToTag.TryGetValue(primaryFlag, out var flagTag);
        string? fewShot = null;
        if (correctionStore != null && flagTag != null)
            fewShot = await correctionStore.GetFewShotAsync(flagTag);

        // Step [7a]: SeedRuleEngine — apply rules on VB method, embed results into
        //            ICSharpCode C# output via SeedRuleCsRewriter (preserves method wrapper)
        var methodVbRoot    = VisualBasicSyntaxTree.ParseText(pair.VbMethodSource).GetRoot();
        var seedMatches     = seedRuleEngine.Apply(methodVbRoot);
        var nodeConfidences = new List<double>();
        string methodCs     = pair.CsMethodSource;

        if (seedMatches.Count > 0)
        {
            foreach (var (_, original, _) in seedMatches)
            {
                seedRuleEngine.TryConvert(original, null, out _, out var conf);
                nodeConfidences.Add(conf);
            }

            var csRoot   = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(methodCs).GetRoot();
            var rewriter = new SeedRuleCsRewriter(seedMatches);
            methodCs = rewriter.Visit(csRoot).ToFullString();
        }

        // Step [7b]: LLM surgical fixer — passes flag + ICSharpCode baseline so LLM
        //            corrects specific patterns instead of re-translating from scratch
        if (seedMatches.Count == 0)
        {
            if (llmTranslator != null)
            {
                var llmResult = await llmTranslator.TranslateAsync(
                    pair.VbMethodSource,
                    csBaseline: pair.CsMethodSource,
                    flagHint:   flagTag ?? primaryFlag,
                    fewShotExample: fewShot);

                if (llmResult.Route != TranslationRoute.HumanQueue)
                {
                    methodCs = llmResult.CsSource;
                    nodeConfidences.Add(llmResult.Confidence);
                }
                else
                {
                    nodeConfidences.Add(0.40);
                }
            }
            else
            {
                nodeConfidences.Add(0.50);
            }
        }

        // Step [7c]: LlmUsingResolver — collect needed namespaces from method C#
        var resolution = usingResolver.Resolve(methodCs, null);

        // Validation and scoring happen at file level after ReassembleFile
        var confidence = ConfidenceScorer.Score(nodeConfidences);
        var route      = seedMatches.Count > 0 ? TranslationRoute.SeedRule : TranslationRoute.Llm;

        return new TranslationResult
        {
            CsSource          = methodCs,
            Confidence        = confidence,
            Route             = route,
            CompilerPassed    = false,  // set at file level after assembly + validation
            ResolvedNamespaces = resolution.Namespaces
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
