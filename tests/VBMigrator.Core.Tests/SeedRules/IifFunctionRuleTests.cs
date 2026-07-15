using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class IifFunctionRuleTests
{
    private readonly IifFunctionRule _rule = new();

    [Fact]
    public void CanHandle_IIfThreeArgs_ReturnsTrue()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = IIf(x > 0, a, b)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        Assert.True(_rule.CanHandle(node));
    }

    [Fact]
    public void Convert_AlwaysEmitsWarningComment()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = IIf(x > 0, a, b)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = _rule.Convert(node).ToString();
        // Warning must ALWAYS appear regardless of side-effect analysis
        Assert.Contains("IIf evalúa ambos brazos", result);
    }

    [Fact]
    public void Convert_ProducesTernaryExpression()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim r = IIf(x > 0, a, b)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("?", result);
        Assert.Contains(":", result);
    }
}
