using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Fixes property name case mismatches in object initializers that arise from
/// VB case-insensitivity: ICSharpCode may emit "Igv = igv" when the C# model
/// defines "IGV". Requires a member index built from all project .cs files.
/// Only fixes object initializer assignments (safe, no semantic model needed).
/// </summary>
public static class PropertyCaseFixer
{
    /// <summary>
    /// Scans all provided C# sources and returns {ClassName → canonical member names}.
    /// </summary>
    public static Dictionary<string, HashSet<string>> BuildMemberIndex(
        IEnumerable<string> csSources)
    {
        var index = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var src in csSources)
        {
            var root = CSharpSyntaxTree.ParseText(src).GetRoot();

            foreach (var typeDecl in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                var name = typeDecl.Identifier.Text;
                if (!index.TryGetValue(name, out var members))
                {
                    members = new HashSet<string>(StringComparer.Ordinal);
                    index[name] = members;
                }

                foreach (var member in typeDecl.Members)
                {
                    switch (member)
                    {
                        case PropertyDeclarationSyntax prop:
                            members.Add(prop.Identifier.Text);
                            break;
                        case FieldDeclarationSyntax field:
                            foreach (var v in field.Declaration.Variables)
                                members.Add(v.Identifier.Text);
                            break;
                    }
                }
            }
        }

        return index;
    }

    /// <summary>
    /// Rewrites object initializer assignments where the property name differs
    /// only in case from a known canonical member name.
    /// </summary>
    public static string Fix(string csSource, Dictionary<string, HashSet<string>> memberIndex)
    {
        if (memberIndex.Count == 0) return csSource;

        var root    = CSharpSyntaxTree.ParseText(csSource).GetRoot();
        var rewriter = new CaseMismatchRewriter(memberIndex);
        var newRoot  = rewriter.Visit(root);
        return newRoot.ToFullString();
    }

    private sealed class CaseMismatchRewriter(Dictionary<string, HashSet<string>> index)
        : CSharpSyntaxRewriter
    {
        // Stack to handle nested object creations
        private readonly Stack<HashSet<string>?> _scopeStack = new();

        public override SyntaxNode? VisitObjectCreationExpression(
            ObjectCreationExpressionSyntax node)
        {
            string? typeName = node.Type switch
            {
                IdentifierNameSyntax id    => id.Identifier.Text,
                QualifiedNameSyntax  qn    => qn.Right.Identifier.Text,
                GenericNameSyntax    gn    => gn.Identifier.Text,
                _                          => null
            };

            index.TryGetValue(typeName ?? string.Empty, out var members);
            _scopeStack.Push(members);

            var result = base.VisitObjectCreationExpression(node);

            _scopeStack.Pop();
            return result;
        }

        public override SyntaxNode? VisitAssignmentExpression(
            AssignmentExpressionSyntax node)
        {
            var currentMembers = _scopeStack.Count > 0 ? _scopeStack.Peek() : null;

            if (currentMembers != null && node.Left is IdentifierNameSyntax id)
            {
                var name = id.Identifier.Text;
                var canonical = currentMembers.FirstOrDefault(m =>
                    string.Equals(m, name, StringComparison.OrdinalIgnoreCase) && m != name);

                if (canonical != null)
                {
                    var newToken = SyntaxFactory.Identifier(
                        id.Identifier.LeadingTrivia,
                        canonical,
                        id.Identifier.TrailingTrivia);
                    node = node.WithLeft(id.WithIdentifier(newToken));
                }
            }

            return base.VisitAssignmentExpression(node);
        }
    }
}
