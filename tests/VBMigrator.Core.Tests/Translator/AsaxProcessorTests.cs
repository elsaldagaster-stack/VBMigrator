using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class AsaxProcessorTests
{
    private static readonly RoslynTranslator _translator = new();

    private const string SampleVbAsax = """
        <%@ Application Language="VB" %>

        <script runat="server">

            Sub Application_Start(ByVal sender As Object, ByVal e As EventArgs)
                ' startup
            End Sub

            Sub Application_Error(ByVal sender As Object, ByVal e As EventArgs)
                Dim ex As Exception = Server.GetLastError()
                If ex IsNot Nothing Then
                    System.Diagnostics.Trace.WriteLine("Error: " & ex.ToString())
                End If
            End Sub

            Sub Session_End(ByVal sender As Object, ByVal e As EventArgs)
            End Sub

        </script>
        """;

    [Fact]
    public async Task ProcessAsync_LanguageDirective_ConvertedToCSharp()
    {
        var result = await AsaxProcessor.ProcessAsync(SampleVbAsax, _translator);
        Assert.Contains("Language=\"C#\"", result);
        Assert.DoesNotContain("Language=\"VB\"", result);
    }

    [Fact]
    public async Task ProcessAsync_ScriptBlock_ContainsCSharpMethods()
    {
        var result = await AsaxProcessor.ProcessAsync(SampleVbAsax, _translator);
        // ICSharpCode should emit void methods, not Sub declarations
        Assert.Contains("void", result);
        Assert.DoesNotContain("Sub ", result);
    }

    [Fact]
    public async Task ProcessAsync_ScriptBlock_PreservesMethodNames()
    {
        var result = await AsaxProcessor.ProcessAsync(SampleVbAsax, _translator);
        Assert.Contains("Application_Start", result);
        Assert.Contains("Application_Error", result);
        Assert.Contains("Session_End", result);
    }

    [Fact]
    public async Task ProcessAsync_ScriptBlock_IsNotNothingConverted()
    {
        var result = await AsaxProcessor.ProcessAsync(SampleVbAsax, _translator);
        // CsOutputFixer should convert "is not null" patterns
        Assert.DoesNotContain("IsNot Nothing", result);
    }

    [Fact]
    public async Task ProcessAsync_ScriptBlock_ScriptTagsPreserved()
    {
        var result = await AsaxProcessor.ProcessAsync(SampleVbAsax, _translator);
        Assert.Contains("<script runat=\"server\">", result, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("</script>", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProcessAsync_VbFakeCharLiteralArray_ConvertedCorrectly()
    {
        // VB '|' inside New Char() {} is actually a comment — pre-processor must fix it
        const string input = """
            <%@ Application Language="VB" %>
            <script runat="server">
                Sub Test(ByVal sender As Object, ByVal e As EventArgs)
                    Dim parts As String() = "a|b".Split(New Char() {'|'})
                End Sub
            </script>
            """;

        var result = await AsaxProcessor.ProcessAsync(input, _translator);
        // Should not contain the mangled ICSharpCode output
        Assert.DoesNotContain("// |'}", result);
        Assert.Contains("Split", result);
        Assert.Contains("|", result);
    }

    [Fact]
    public async Task ProcessAsync_NoCSharpMarkup_ReturnsUnchanged()
    {
        const string csharpAsax = """
            <%@ Application Language="C#" %>
            <script runat="server">
                void Application_Start(object sender, EventArgs e) { }
            </script>
            """;

        var result = await AsaxProcessor.ProcessAsync(csharpAsax, _translator);
        Assert.Equal(csharpAsax, result);
    }

    [Fact]
    public async Task ProcessAsync_NoScriptBlock_OnlyDirectiveFixed()
    {
        const string input = "<%@ Application Language=\"VB\" %>";
        var result = await AsaxProcessor.ProcessAsync(input, _translator);
        Assert.Contains("Language=\"C#\"", result);
    }
}
