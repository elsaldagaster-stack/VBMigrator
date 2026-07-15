using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class LogicalOperatorRulesTests
{
    [Fact]
    public void AndAlsoRule_Convert_ProducesDoubleAmpersand()
    {
        var rule = new AndAlsoRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = a AndAlso c\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.AndAlsoExpression));
        Assert.True(rule.CanHandle(node));
        var result = rule.Convert(node);
        Assert.Contains("&&", result.ToString());
    }

    [Fact]
    public void OrElseRule_Convert_ProducesDoublePipe()
    {
        var rule = new OrElseRule();
        var tree = SyntaxFactory.ParseSyntaxTree("Module M\nSub F()\nDim b = a OrElse c\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.OrElseExpression));
        Assert.True(rule.CanHandle(node));
        var result = rule.Convert(node);
        Assert.Contains("||", result.ToString());
    }
}
