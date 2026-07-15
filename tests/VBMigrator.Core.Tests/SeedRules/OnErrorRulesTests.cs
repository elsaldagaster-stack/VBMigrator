using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;
using Xunit.Abstractions;

namespace VBMigrator.Core.Tests.SeedRules;

public class OnErrorRulesTests(ITestOutputHelper output)
{
    [Fact]
    public void OnErrorGotoRule_CanHandle_OnErrorGoTo_ReturnsTrue()
    {
        var rule = new OnErrorGotoRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nOn Error GoTo ErrHandler\nErrHandler:\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<OnErrorGoToStatementSyntax>()
            .First();
        output.WriteLine($"Node kind: {node.Kind()}");
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void OnErrorGotoRule_CanHandle_WithGotoCrossBlock_ReturnsFalse()
    {
        var rule = new OnErrorGotoRule { HasGotoCrossBlock = true };
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nOn Error GoTo ErrHandler\nErrHandler:\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<OnErrorGoToStatementSyntax>()
            .First();
        Assert.False(rule.CanHandle(node));
    }

    [Fact]
    public void OnErrorGotoRule_Convert_ProducesTryCatch()
    {
        var rule = new OnErrorGotoRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nOn Error GoTo ErrHandler\nErrHandler:\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<OnErrorGoToStatementSyntax>()
            .First();
        var result = rule.Convert(node).ToString();
        Assert.Contains("try", result);
        Assert.Contains("catch", result);
        Assert.Contains("Exception ex", result);
        Assert.Contains("ex.Message", result);
    }

    [Fact]
    public void OnErrorResumeRule_CanHandle_OnErrorResumeNext_ReturnsTrue()
    {
        var rule = new OnErrorResumeRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nOn Error Resume Next\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<OnErrorResumeNextStatementSyntax>()
            .First();
        output.WriteLine($"Node kind: {node.Kind()}");
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void OnErrorResumeRule_Convert_EmitsHumanQueueMarker()
    {
        var rule = new OnErrorResumeRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nOn Error Resume Next\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<OnErrorResumeNextStatementSyntax>()
            .First();
        var result = rule.Convert(node).ToString();
        Assert.Contains("On Error Resume Next", result);
        Assert.Contains("revisión manual", result);
        Assert.Contains("HUMAN_QUEUE", result);
    }
}
