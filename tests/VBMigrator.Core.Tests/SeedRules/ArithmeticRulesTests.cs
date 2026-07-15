using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class ArithmeticRulesTests
{
    [Fact]
    public void IntegerDivisionRule_CanHandle_BackslashExpression_ReturnsTrue()
    {
        var rule = new IntegerDivisionRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = x \\ y\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IntegerDivideExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void IntegerDivisionRule_Convert_ProducesCastDivision()
    {
        var rule = new IntegerDivisionRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = x \\ y\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.IntegerDivideExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("(int)", result);
        Assert.Contains("x / y", result);
    }

    [Fact]
    public void ExponentiationRule_CanHandle_CaretExpression_ReturnsTrue()
    {
        var rule = new ExponentiationRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = x ^ y\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ExponentiateExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void ExponentiationRule_Convert_ProducesMathPow()
    {
        var rule = new ExponentiationRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = x ^ y\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ExponentiateExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("Math.Pow", result);
        Assert.Contains("x", result);
        Assert.Contains("y", result);
    }
}
