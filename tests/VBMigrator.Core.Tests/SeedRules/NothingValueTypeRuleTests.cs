using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using VBMigrator.Core.SeedRules.Rules;
using Xunit;

namespace VBMigrator.Core.Tests.SeedRules;

public class NothingValueTypeRuleTests
{
    private readonly NothingValueTypeRule _rule = new();

    [Fact]
    public void CanHandle_WithoutSemanticModel_ReturnsFalse()
    {
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim x As Integer = Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.LocalDeclarationStatement));
        Assert.False(_rule.CanHandle(node, semanticModel: null));
    }

    [Fact]
    public void CanHandle_NothingLiteralWithValueType_AndSemanticModel_ReturnsTrue()
    {
        var vbCode = @"
Module M
    Sub F()
        Dim x As Integer = Nothing
    End Sub
End Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vbCode);
        var compilation = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create(
            "TestAsm",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.LocalDeclarationStatement));
        Assert.True(_rule.CanHandle(node, model));
    }

    [Fact]
    public void Convert_IntegerNothing_ProducesIntDefault()
    {
        var tree = SyntaxFactory.ParseSyntaxTree(
            "Module M\nSub F()\nDim x As Integer = Nothing\nEnd Sub\nEnd Module");
        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.LocalDeclarationStatement));
        var result = _rule.Convert(node).ToString();
        Assert.Contains("int", result);
        Assert.Contains("x", result);
        Assert.Contains("default", result);
    }

    [Fact]
    public void CanHandle_NothingLiteralWithReferenceType_ReturnsFalse()
    {
        var vbCode = @"
Module M
    Sub F()
        Dim x As String = Nothing
    End Sub
End Module";
        var tree = SyntaxFactory.ParseSyntaxTree(vbCode);
        var compilation = Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilation.Create(
            "TestAsm2",
            new[] { tree },
            new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
            new Microsoft.CodeAnalysis.VisualBasic.VisualBasicCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);

        var node = tree.GetRoot().DescendantNodes()
            .First(n => n.IsKind(SyntaxKind.LocalDeclarationStatement));
        Assert.False(_rule.CanHandle(node, model));
    }
}
