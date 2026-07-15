using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using System.Text;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// like_operator: s Like "pattern"  →  Regex.IsMatch(s, translatedPattern)
/// Wildcard mapping: * → .*, ? → ., # → \d, [abc] → [abc] (preserved)
/// Anchors: always wraps with ^ ... $ to match VB Like semantics (full-string match).
/// </summary>
public sealed class LikeOperatorRule : ISeedRule
{
    public string Tag => "like_operator";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
        => node is BinaryExpressionSyntax bin
           && bin.IsKind(SyntaxKind.LikeExpression);

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var bin     = (BinaryExpressionSyntax)node;
        var subject = bin.Left.ToString().Trim();
        var pattern = bin.Right.ToString().Trim();

        string regexPattern;
        if (pattern.StartsWith("\"") && pattern.EndsWith("\"") && pattern.Length >= 2)
        {
            var inner = pattern[1..^1];
            regexPattern = "\"^" + TranslateWildcards(inner) + "$\"";
        }
        else
        {
            regexPattern = $"/* VBMigrator: translate Like pattern */ {pattern}";
        }

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"System.Text.RegularExpressions.Regex.IsMatch({subject}, {regexPattern})");
    }

    public static string TranslateWildcards(string vbPattern)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i < vbPattern.Length)
        {
            char c = vbPattern[i];
            switch (c)
            {
                case '*':
                    sb.Append(".*");
                    i++;
                    break;
                case '?':
                    sb.Append('.');
                    i++;
                    break;
                case '#':
                    sb.Append(@"\d");
                    i++;
                    break;
                case '[':
                    int close = vbPattern.IndexOf(']', i + 1);
                    if (close >= 0)
                    {
                        sb.Append(vbPattern[i..(close + 1)]);
                        i = close + 1;
                    }
                    else
                    {
                        sb.Append(@"\[");
                        i++;
                    }
                    break;
                default:
                    sb.Append(System.Text.RegularExpressions.Regex.Escape(c.ToString()));
                    i++;
                    break;
            }
        }
        return sb.ToString();
    }
}
