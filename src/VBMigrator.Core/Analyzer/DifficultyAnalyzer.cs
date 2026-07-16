using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VBMigrator.Core.Analyzer.FlagDetectors;
using VBMigrator.Core.Models;

namespace VBMigrator.Core.Analyzer;

/// <summary>
/// Analyzes a VB.NET SyntaxTree and produces a DifficultyMap.
/// The map contains per-method flags used by the pipeline to decide routing.
/// </summary>
public class DifficultyAnalyzer
{
    public DifficultyMap Analyze(SyntaxTree tree, string filePath, SemanticModel? semanticModel = null)
    {
        var root = tree.GetRoot();
        var functions = new List<FunctionDifficulty>();

        foreach (var method in GetMethodBlocks(root))
        {
            functions.Add(AnalyzeMethod(method, semanticModel));
        }

        int overallScore = functions.Count == 0 ? 0 : (int)functions.Average(f => f.Score);

        return new DifficultyMap
        {
            FilePath     = filePath,
            OverallScore = overallScore,
            Functions    = functions
        };
    }

    private static FunctionDifficulty AnalyzeMethod(SyntaxNode methodNode, SemanticModel? semanticModel)
    {
        var flags = new List<string>();
        var name  = GetMethodName(methodNode);

        if (WithEventsDetector.HasWithEventsInScope(methodNode))
            flags.Add(WithEventsDetector.Flag);

        var (hasOnError, hasOnErrorResume, hasGotoCrossBlock) = OnErrorDetector.Detect(methodNode);
        if (hasOnError)        flags.Add(OnErrorDetector.FlagOnError);
        if (hasOnErrorResume)  flags.Add(OnErrorDetector.FlagOnErrorResume);
        if (hasGotoCrossBlock) flags.Add(OnErrorDetector.FlagGotoCrossBlock);

        if (MyNamespaceDetector.HasMyNamespace(methodNode))
            flags.Add(MyNamespaceDetector.Flag);

        if (LateBindingDetector.HasLateBinding(methodNode, semanticModel))
            flags.Add(LateBindingDetector.Flag);

        var (likeOp, exponOp, byteLoop, iifOp) = OperatorDetector.Detect(methodNode);
        if (likeOp)   flags.Add(OperatorDetector.FlagLikeOp);
        if (exponOp)  flags.Add(OperatorDetector.FlagExponOp);
        if (byteLoop) flags.Add(OperatorDetector.FlagByteLoop);
        if (iifOp)    flags.Add(OperatorDetector.FlagIifOp);

        int score = Math.Min(100, flags.Count * 10);
        var route = DetermineRoute(flags);

        return new FunctionDifficulty
        {
            MethodName = name,
            Score      = score,
            Flags      = flags,
            Route      = route
        };
    }

    private static TranslationRoute DetermineRoute(List<string> flags)
    {
        if (flags.Contains(OnErrorDetector.FlagGotoCrossBlock))
            return TranslationRoute.HumanQueue;
        if (flags.Count == 0)
            return TranslationRoute.SeedRule;
        if (flags.Contains(LateBindingDetector.Flag) || flags.Contains(WithEventsDetector.Flag))
            return TranslationRoute.Llm;
        return TranslationRoute.SeedRule;
    }

    private static IEnumerable<SyntaxNode> GetMethodBlocks(SyntaxNode root)
        => root.DescendantNodes().Where(n =>
            n is MethodBlockSyntax or
                 ConstructorBlockSyntax or
                 PropertyBlockSyntax or
                 OperatorBlockSyntax);

    private static string GetMethodName(SyntaxNode node) => node switch
    {
        MethodBlockSyntax mb      => mb.SubOrFunctionStatement.Identifier.Text,
        ConstructorBlockSyntax cb => cb.SubNewStatement.NewKeyword.Text,
        PropertyBlockSyntax pb    => pb.PropertyStatement.Identifier.Text,
        OperatorBlockSyntax ob    => ob.OperatorStatement.OperatorToken.Text,
        _                         => "<unknown>"
    };
}
