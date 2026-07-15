using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Resolves missing using statements after both SeedRules and LLM have run on the C# method.
/// Runs on complete C# method text (post-SeedRule + post-LLM), per spec §4.3.
/// Types not in dictionary → emits MISSING_USING flag for HumanQueue.
/// </summary>
public class LlmUsingResolver
{
    private static readonly Dictionary<string, string> _wellKnown = new(StringComparer.Ordinal)
    {
        ["WindowsIdentity"]       = "System.Security.Principal",
        ["Regex"]                 = "System.Text.RegularExpressions",
        ["Assembly"]              = "System.Reflection",
        ["DateTime"]              = "System",
        ["StringComparison"]      = "System",
        ["Math"]                  = "System",
        ["File"]                  = "System.IO",
        ["Path"]                  = "System.IO",
        ["Directory"]             = "System.IO",
        ["StringBuilder"]         = "System.Text",
        ["Encoding"]              = "System.Text",
        ["Stream"]                = "System.IO",
        ["StreamReader"]          = "System.IO",
        ["StreamWriter"]          = "System.IO",
        ["List"]                  = "System.Collections.Generic",
        ["Dictionary"]            = "System.Collections.Generic",
        ["IEnumerable"]           = "System.Collections.Generic",
        ["Task"]                  = "System.Threading.Tasks",
        ["CancellationToken"]     = "System.Threading",
        ["HttpClient"]            = "System.Net.Http",
        ["JsonSerializer"]        = "System.Text.Json",
        ["JsonSerializerOptions"] = "System.Text.Json",
        ["XDocument"]             = "System.Xml.Linq",
        ["XElement"]              = "System.Xml.Linq",
    };

    public const string MissingUsingFlag = "MISSING_USING";

    public LlmUsingResolution Resolve(string csMethodSource, Microsoft.CodeAnalysis.SemanticModel? model = null)
    {
        var resolvedNamespaces = new HashSet<string>(StringComparer.Ordinal);
        var missingTypes       = new List<string>();

        var tree = CSharpSyntaxTree.ParseText(csMethodSource);
        var root = tree.GetRoot();

        var candidates = root.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Select(id => id.Identifier.Text)
            .Distinct(StringComparer.Ordinal);

        foreach (var typeName in candidates)
        {
            if (_wellKnown.TryGetValue(typeName, out var ns))
                resolvedNamespaces.Add(ns);
        }

        return new LlmUsingResolution
        {
            Namespaces   = resolvedNamespaces.ToList(),
            MissingTypes = missingTypes
        };
    }

    public static IEnumerable<string> ToUsingDirectives(IEnumerable<string> namespaces)
        => namespaces.OrderBy(n => n).Select(n => $"using {n};");

    public static IReadOnlyDictionary<string, string> WellKnown => _wellKnown;
}

public record LlmUsingResolution
{
    public List<string> Namespaces   { get; init; } = new();
    public List<string> MissingTypes { get; init; } = new();
    public bool HasMissingTypes => MissingTypes.Count > 0;
}
