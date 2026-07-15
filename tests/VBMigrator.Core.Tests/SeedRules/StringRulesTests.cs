using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class StringRulesTests
{
    [Fact]
    public void StringConcatRule_CanHandle_AmpersandExpression_ReturnsTrue()
    {
        var rule = new StringConcatRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim s = a & b\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ConcatenateExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void StringConcatRule_Convert_ProducesPlusOperator()
    {
        var rule = new StringConcatRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim s = a & b\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ConcatenateExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("+", result);
        Assert.False(result.Contains("&"), "should not contain VB & operator");
    }

    [Fact]
    public void StringConcatRule_Convert_NumericHeuristicName_SetsNumericFlag()
    {
        var rule = new StringConcatRule();
        // 'count' is in the numeric hints set
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim s = count & b\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ConcatenateExpression));
        rule.Convert(node, semanticModel: null);
        Assert.True(rule.LastConvertHadNumericOperand);
    }

    [Fact]
    public void StringComparisonRule_CanHandle_ThreeArgStringCompareTrue_ReturnsTrue()
    {
        var rule = new StringComparisonRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = String.Compare(a, b, True)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void StringComparisonRule_Convert_True_ProducesOrdinalIgnoreCase()
    {
        var rule = new StringComparisonRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = String.Compare(a, b, True)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("OrdinalIgnoreCase", result);
    }

    [Fact]
    public void StringComparisonRule_Convert_False_ProducesOrdinal()
    {
        var rule = new StringComparisonRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = String.Compare(a, b, False)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("StringComparison.Ordinal", result);
        Assert.DoesNotContain("IgnoreCase", result);
    }
}
