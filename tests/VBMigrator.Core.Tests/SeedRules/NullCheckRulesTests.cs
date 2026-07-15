using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class NullCheckRulesTests
{
    [Fact]
    public void IsNothingRule_CanHandle_BinaryIsNothing_ReturnsTrue()
    {
        var rule = new IsNothingRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = x Is Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IsExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void IsNothingRule_Convert_ProducesIsNull()
    {
        var rule = new IsNothingRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = x Is Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IsExpression));
        var result = rule.Convert(node);
        Assert.Contains("is null", result.ToString());
    }

    [Fact]
    public void IsNotNothingRule_CanHandle_BinaryIsNotNothing_ReturnsTrue()
    {
        var rule = new IsNotNothingRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = x IsNot Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IsNotExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void IsNotNothingRule_Convert_ProducesIsNotNull()
    {
        var rule = new IsNotNothingRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = x IsNot Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IsNotExpression));
        var result = rule.Convert(node);
        Assert.Contains("is not null", result.ToString());
    }
}
