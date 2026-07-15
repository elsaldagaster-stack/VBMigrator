using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VisualBasicSyntaxKind = Microsoft.CodeAnalysis.VisualBasic.SyntaxKind;
using System.Text.RegularExpressions;

namespace VBMigrator.Core.Learning;

public static class PatternNormalizer
{
    public record NormMap(
        Dictionary<string, string> IdentifierMap,  // original → __varN__
        Dictionary<string, string> TypeMap);        // original → __TypeN__

    public static (string Template, NormMap Map) NormalizeVb(string vbSnippet)
    {
        var tree = VisualBasicSyntaxTree.ParseText(vbSnippet);
        var root = tree.GetRoot();

        var identMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var typeMap  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int varCounter  = 0;
        int typeCounter = 0;

        // Keyword types (Integer, String, Boolean, etc.)
        foreach (var predefined in root.DescendantNodes().OfType<PredefinedTypeSyntax>())
        {
            var text = predefined.Keyword.Text;
            if (!typeMap.ContainsKey(text))
                typeMap[text] = $"__Type{++typeCounter}__";
        }

        // Custom types from As clauses (e.g., As MyClass)
        foreach (var asClause in root.DescendantNodes().OfType<SimpleAsClauseSyntax>())
        {
            if (asClause.Type is IdentifierNameSyntax typeName)
            {
                var text = typeName.Identifier.Text;
                if (!typeMap.ContainsKey(text))
                    typeMap[text] = $"__Type{++typeCounter}__";
            }
        }

        // Declared variable names from ModifiedIdentifier (Dim x As ...)
        foreach (var modId in root.DescendantNodes().OfType<ModifiedIdentifierSyntax>())
        {
            var text = modId.Identifier.Text;
            if (!identMap.ContainsKey(text) && !typeMap.ContainsKey(text))
                identMap[text] = $"__var{++varCounter}__";
        }

        // Expression identifiers (usages: x + y, GoTo ErrHandler, etc.)
        foreach (var idName in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            // Skip if this identifier is serving as a type in an As clause
            if (idName.Parent is SimpleAsClauseSyntax)
                continue;

            var text = idName.Identifier.Text;
            if (!identMap.ContainsKey(text) && !typeMap.ContainsKey(text))
                identMap[text] = $"__var{++varCounter}__";
        }

        // Label targets (e.g., GoTo ErrHandler — LabelSyntax)
        foreach (var token in root.DescendantTokens()
                     .Where(t => t.IsKind(VisualBasicSyntaxKind.IdentifierToken)))
        {
            var text = token.Text;
            if (!identMap.ContainsKey(text) && !typeMap.ContainsKey(text))
                identMap[text] = $"__var{++varCounter}__";
        }

        var map = new NormMap(identMap, typeMap);
        return (ApplyMap(vbSnippet, map), map);
    }

    public static (string Template, NormMap Map) NormalizeCs(string csSnippet, NormMap vbMap)
    {
        var tree = CSharpSyntaxTree.ParseText(csSnippet);
        var root = tree.GetRoot();

        int newCounter = 0;
        var csIntroduced = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var id in root.DescendantTokens()
                     .Where(t => t.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken)))
        {
            var text = id.Text;
            if (vbMap.IdentifierMap.ContainsKey(text) || vbMap.TypeMap.ContainsKey(text))
                continue;
            if (!csIntroduced.ContainsKey(text))
                csIntroduced[text] = $"__new{++newCounter}__";
        }

        var merged = new NormMap(
            new Dictionary<string, string>(
                vbMap.IdentifierMap.Concat(csIntroduced),
                StringComparer.OrdinalIgnoreCase),
            vbMap.TypeMap);

        return (ApplyMap(csSnippet, merged), merged);
    }

    private static string ApplyMap(string source, NormMap map)
    {
        var result = source;
        foreach (var (orig, replacement) in map.IdentifierMap
                     .Concat(map.TypeMap)
                     .OrderByDescending(p => p.Key.Length))
        {
            result = Regex.Replace(
                result,
                $@"\b{Regex.Escape(orig)}\b",
                replacement);
        }
        return result;
    }
}
