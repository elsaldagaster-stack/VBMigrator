using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class ArrayRulesTests
{
    [Fact]
    public void RedimPreserveRule_CanHandle_ReDimPreserve_ReturnsTrue()
    {
        var rule = new RedimPreserveRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nReDim Preserve arr(n)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ReDimPreserveStatement));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void RedimPreserveRule_Convert_ProducesArrayResize()
    {
        var rule = new RedimPreserveRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nReDim Preserve arr(n)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ReDimPreserveStatement));
        var result = rule.Convert(node).ToString();
        Assert.Contains("Array.Resize", result);
        Assert.Contains("ref arr", result);
        Assert.Contains("n + 1", result);
    }

    [Fact]
    public void EraseArrayRule_CanHandle_EraseStatement_ReturnsTrue()
    {
        var rule = new EraseArrayRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nErase arr\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.EraseStatement));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void EraseArrayRule_Convert_ProducesNullAssignment()
    {
        var rule = new EraseArrayRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nErase arr\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.EraseStatement));
        var result = rule.Convert(node).ToString();
        Assert.Contains("arr", result);
        Assert.Contains("null", result);
    }
}
