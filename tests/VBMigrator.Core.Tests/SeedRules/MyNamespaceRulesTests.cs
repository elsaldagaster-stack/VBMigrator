using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class MyNamespaceRulesTests
{
    [Fact]
    public void MySettingsRule_CanHandle_MySettingsFoo_ReturnsTrue()
    {
        var rule = new MySettingsRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim v = My.Settings.Foo\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(n => n.ToString().StartsWith("My.Settings."));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void MySettingsRule_Convert_ProducesPropertiesSettingsDefault()
    {
        var rule = new MySettingsRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim v = My.Settings.Foo\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(n => n.ToString().StartsWith("My.Settings."));
        var result = rule.Convert(node).ToString();
        Assert.Contains("Properties.Settings.Default.Foo", result);
    }

    [Fact]
    public void MyFilesystemReadRule_CanHandle_ReadAllText_ReturnsTrue()
    {
        var rule = new MyFilesystemReadRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim s = My.Computer.FileSystem.ReadAllText(\"f.txt\")\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void MyFilesystemReadRule_Convert_ProducesFileReadAllText()
    {
        var rule = new MyFilesystemReadRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim s = My.Computer.FileSystem.ReadAllText(\"f.txt\")\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("System.IO.File.ReadAllText", result);
    }

    [Fact]
    public void MyFilesystemWriteRule_CanHandle_WriteAllText_ReturnsTrue()
    {
        var rule = new MyFilesystemWriteRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nMy.Computer.FileSystem.WriteAllText(\"f.txt\", s)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void MyFilesystemWriteRule_Convert_ProducesFileWriteAllText()
    {
        var rule = new MyFilesystemWriteRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nMy.Computer.FileSystem.WriteAllText(\"f.txt\", s)\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.InvocationExpression));
        var result = rule.Convert(node).ToString();
        Assert.Contains("System.IO.File.WriteAllText", result);
    }

    [Fact]
    public void MyAppVersionRule_CanHandle_MyApplicationInfoVersion_ReturnsTrue()
    {
        var rule = new MyAppVersionRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim v = My.Application.Info.Version\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(n => n.ToString() == "My.Application.Info.Version");
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void MyAppVersionRule_Convert_ProducesAssemblyGetName()
    {
        var rule = new MyAppVersionRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim v = My.Application.Info.Version\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(n => n.ToString() == "My.Application.Info.Version");
        var result = rule.Convert(node).ToString();
        Assert.Contains("Assembly.GetExecutingAssembly", result);
        Assert.Contains("GetName", result);
        Assert.Contains("Version", result);
    }

    [Fact]
    public void MyUserRule_CanHandle_MyUserName_ReturnsTrue()
    {
        var rule = new MyUserRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim n = My.User.Name\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(n => n.ToString() == "My.User.Name");
        Assert.True(rule.CanHandle(node));
    }

    [Fact]
    public void MyUserRule_Convert_ProducesWindowsIdentityGetCurrent()
    {
        var rule = new MyUserRule();
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim n = My.User.Name\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .OfType<MemberAccessExpressionSyntax>()
            .First(n => n.ToString() == "My.User.Name");
        var result = rule.Convert(node).ToString();
        Assert.Contains("WindowsIdentity.GetCurrent", result);
        Assert.Contains(".Name", result);
    }
}
