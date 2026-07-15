using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class RoslynTranslatorTests
{
    private readonly RoslynTranslator _translator = new();

    [Fact]
    public async Task ConvertFileAsync_SimpleModule_ReturnsNonEmptyCSharp()
    {
        const string vb = @"
Module HelloModule
    Sub SayHello()
        Dim message As String = ""Hello, World!""
        Console.WriteLine(message)
    End Sub
End Module";

        var result = await _translator.ConvertFileAsync(vb, "Hello.vb");

        Assert.True(result.Success, $"Conversion failed: {string.Join("; ", result.Errors)}");
        Assert.NotEmpty(result.CsSource);
        Assert.Contains("static", result.CsSource);
    }

    [Fact]
    public async Task ConvertFileAsync_EmptySource_ReturnsFalseOrEmpty()
    {
        var result = await _translator.ConvertFileAsync(string.Empty, "empty.vb");
        Assert.True(!result.Success || string.IsNullOrWhiteSpace(result.CsSource));
    }

    [Fact]
    public async Task ConvertFileAsync_ClassWithProperty_ContainsPropertyInOutput()
    {
        const string vb = @"
Public Class Person
    Public Property Name As String
End Class";

        var result = await _translator.ConvertFileAsync(vb, "Person.vb");

        Assert.True(result.Success, $"Conversion failed: {string.Join("; ", result.Errors)}");
        Assert.Contains("Name", result.CsSource);
    }
}
