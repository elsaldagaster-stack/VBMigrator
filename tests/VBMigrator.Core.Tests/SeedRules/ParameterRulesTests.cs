using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class ParameterRulesTests
{
    [Fact]
    public void ByValParamRule_CanHandle_ByValParameter_ReturnsTrue()
    {
        var rule = new ByValParamRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F(ByVal x As Integer)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.Parameter));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void ByValParamRule_Convert_RemovesByValModifier()
    {
        var rule = new ByValParamRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F(ByVal x As Integer)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.Parameter));
        var result = rule.Convert(node).ToString();
        Assert.DoesNotContain("ByVal", result);
        Assert.Contains("Integer", result);
        Assert.Contains("x", result);
    }

    [Fact]
    public void OptionalParamRule_CanHandle_OptionalParameter_ReturnsTrue()
    {
        var rule = new OptionalParamRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F(Optional ByVal x As Integer = 0)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.Parameter));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void OptionalParamRule_Convert_ProducesDefaultValue()
    {
        var rule = new OptionalParamRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F(Optional ByVal x As Integer = 0)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.Parameter));
        var result = rule.Convert(node).ToString();
        Assert.Contains("= 0", result);
        Assert.DoesNotContain("Optional", result);
    }
}
