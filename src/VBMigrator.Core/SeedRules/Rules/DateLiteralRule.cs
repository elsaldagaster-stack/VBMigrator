using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using System.Text.RegularExpressions;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// date_literal: #2020-01-01#  →  new DateTime(2020, 1, 1)
/// Uses regex to extract year/month/day from the literal text.
/// Supports formats: #yyyy-MM-dd# and #M/d/yyyy#
/// </summary>
public sealed class DateLiteralRule : ISeedRule
{
    public string Tag => "date_literal";
    public int Priority => 100;

    private static readonly Regex _iso   = new(@"#(\d{4})-(\d{1,2})-(\d{1,2})#", RegexOptions.Compiled);
    private static readonly Regex _slash = new(@"#(\d{1,2})/(\d{1,2})/(\d{4})#", RegexOptions.Compiled);

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not LiteralExpressionSyntax lit) return false;
        if (!lit.IsKind(SyntaxKind.DateLiteralExpression)) return false;
        var text = lit.ToString();
        return _iso.IsMatch(text) || _slash.IsMatch(text);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var lit  = (LiteralExpressionSyntax)node;
        var text = lit.ToString();

        int year, month, day;
        var isoMatch = _iso.Match(text);
        if (isoMatch.Success)
        {
            year  = int.Parse(isoMatch.Groups[1].Value);
            month = int.Parse(isoMatch.Groups[2].Value);
            day   = int.Parse(isoMatch.Groups[3].Value);
        }
        else
        {
            var slashMatch = _slash.Match(text);
            month = int.Parse(slashMatch.Groups[1].Value);
            day   = int.Parse(slashMatch.Groups[2].Value);
            year  = int.Parse(slashMatch.Groups[3].Value);
        }

        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"new DateTime({year}, {month}, {day})");
    }
}
