using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules;

/// <summary>
/// Applies registered ISeedRules to all descendant nodes of a VB method block.
/// Rules are ordered by Priority descending; first match per node wins.
/// Operates on Roslyn VB SyntaxTree nodes (never C# nodes).
/// </summary>
public class SeedRuleEngine
{
    private readonly IReadOnlyList<ISeedRule> _rules;

    public SeedRuleEngine(IEnumerable<ISeedRule> rules)
    {
        _rules = rules.OrderByDescending(r => r.Priority).ToList();
    }

    /// <summary>
    /// Walks all descendant nodes of <paramref name="methodNode"/> (a VB method block).
    /// For each node, the first rule whose CanHandle returns true wins and Convert is called.
    /// Returns a list of (tag, convertedNode) pairs for all matched nodes.
    /// </summary>
    public IReadOnlyList<(string Tag, SyntaxNode Original, SyntaxNode Converted)> Apply(
        SyntaxNode methodNode,
        SemanticModel? semanticModel = null)
    {
        var results = new List<(string, SyntaxNode, SyntaxNode)>();

        foreach (var node in methodNode.DescendantNodesAndSelf())
        {
            foreach (var rule in _rules)
            {
                if (rule.CanHandle(node, semanticModel))
                {
                    var converted = rule.Convert(node, semanticModel);
                    results.Add((rule.Tag, node, converted));
                    break; // first match wins per node
                }
            }
        }

        return results;
    }

    public IReadOnlyList<ISeedRule> Rules => _rules;
}
