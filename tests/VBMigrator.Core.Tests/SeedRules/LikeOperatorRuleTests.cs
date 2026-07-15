using Microsoft.CodeAnalysis;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class LikeOperatorRuleTests
{
    [Theory]
    [InlineData("A*",   "^A.*$")]
    [InlineData("A?B",  "^A.B$")]
    [InlineData("##",   @"^\d\d$")]
    [InlineData("[abc]","^[abc]$")]
    [InlineData("A*B?", "^A.*B.$")]
    public void TranslateWildcards_MapsCorrectly(string vbPattern, string expectedRegex)
    {
        var result = LikeOperatorRule.TranslateWildcards(vbPattern);
        Assert.Equal(expectedRegex[1..^1], result); // strip anchors added by Convert, not TranslateWildcards
    }

    [Fact]
    public void CanHandle_LikeExpression_ReturnsTrue()
    {
        var rule = new LikeOperatorRule();
        var tree = Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory
            .ParseSyntaxTree("Module M\nSub F()\nDim r = s Like \"A*\"\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.LikeExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void Convert_LiteralPattern_ProducesRegexIsMatch()
    {
        var rule = new LikeOperatorRule();
        var tree = Microsoft.CodeAnalysis.VisualBasic.SyntaxFactory
            .ParseSyntaxTree("Module M\nSub F()\nDim r = s Like \"A*\"\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(Microsoft.CodeAnalysis.VisualBasic.SyntaxKind.LikeExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("Regex.IsMatch", result);
        Assert.Contains(".*", result);
    }
}
