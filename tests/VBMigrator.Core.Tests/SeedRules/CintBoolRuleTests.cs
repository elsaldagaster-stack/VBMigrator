using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class CintBoolRuleTests
{
    private readonly CintBoolRule _rule = new();

    [Fact]
    public void CanHandle_CIntTrue_ReturnsTrue()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim x = CInt(True)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.PredefinedCastExpression));
        Assert.True(_rule.CanHandle(node));
    }

    [Fact]
    public void Convert_CIntTrue_ProducesMinusOneExpression()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim x = CInt(True)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.PredefinedCastExpression));
        // Comments live in trivia → use ToFullString() to include them
        var result = _rule.Convert(node).ToFullString();
        Assert.Contains("-1", result);
        Assert.Contains("VB: CInt(True) = -1", result);
    }

    [Fact]
    public void Convert_CIntFalse_ProducesZero()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim x = CInt(False)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.PredefinedCastExpression));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("0", result);
    }

    [Fact]
    public void CanHandle_CIntNumericArg_ReturnsFalse()
    {
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim x = CInt(42)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.PredefinedCastExpression));
        Assert.False(_rule.CanHandle(node));
    }
}
