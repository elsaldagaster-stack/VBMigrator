using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class ByteLoopRuleTests
{
    private readonly ByteLoopRule _rule = new();

    [Fact]
    public void CanHandle_ByteTo255_ReturnsTrue()
    {
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nFor i As Byte = 0 To 255\nNext\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ForStatement));
        Assert.True(_rule.CanHandle(node));
    }

    [Fact]
    public void CanHandle_ByteTo254_ReturnsFalse()
    {
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nFor i As Byte = 0 To 254\nNext\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ForStatement));
        Assert.False(_rule.CanHandle(node));
    }

    [Fact]
    public void CanHandle_IntTo255_ReturnsFalse()
    {
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nFor i As Integer = 0 To 255\nNext\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ForStatement));
        Assert.False(_rule.CanHandle(node));
    }

    [Fact]
    public void Convert_EmitsHumanQueueMarker()
    {
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nFor i As Byte = 0 To 255\nNext\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.ForStatement));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("byte loop", result);
        Assert.Contains("BYTE_LOOP_OVERFLOW", result);
    }
}
