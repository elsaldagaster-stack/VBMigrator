using Microsoft.CodeAnalysis;

namespace VBMigrator.Core.SeedRules;

public interface ISeedRule
{
    string Tag { get; }
    int Priority { get; }
    bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null);
    SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null);
}
