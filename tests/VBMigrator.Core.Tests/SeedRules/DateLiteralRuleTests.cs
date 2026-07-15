using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class DateLiteralRuleTests
{
    private readonly DateLiteralRule _rule = new();

    [Fact]
    public void CanHandle_IsoDateLiteral_ReturnsTrue()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim d = #2020-01-15#\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.DateLiteralExpression));
        Assert.True(_rule.CanHandle(node));
    }

    [Fact]
    public void Convert_IsoDate_ProducesNewDateTime()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim d = #2020-01-15#\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.DateLiteralExpression));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("new DateTime", result);
        Assert.Contains("2020", result);
        Assert.Contains("1", result);
        Assert.Contains("15", result);
    }

    [Fact]
    public void Convert_SlashDate_ProducesNewDateTime()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim d = #1/15/2020#\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.DateLiteralExpression));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("new DateTime", result);
        Assert.Contains("2020", result);
    }
}
