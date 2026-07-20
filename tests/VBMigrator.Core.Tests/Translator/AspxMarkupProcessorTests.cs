using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class AspxMarkupProcessorTests
{
    // ── Directive transforms ─────────────────────────────────────────────────

    [Fact]
    public void Process_LanguageVB_ConvertedToCSharp()
    {
        const string input = "<%@ Page Language=\"VB\" %>";
        Assert.Contains("Language=\"C#\"", AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_CodeFileVb_ConvertedToCs()
    {
        const string input = "<%@ Page CodeFile=\"Default.aspx.vb\" %>";
        var result = AspxMarkupProcessor.Process(input);
        Assert.Contains("Default.aspx.cs", result);
        Assert.DoesNotContain(".aspx.vb", result);
    }

    [Fact]
    public void Process_CodeBehindVb_ConvertedToCs()
    {
        const string input = "<%@ Page CodeBehind=\"Login.aspx.vb\" %>";
        var result = AspxMarkupProcessor.Process(input);
        Assert.Contains("Login.aspx.cs", result);
        Assert.DoesNotContain(".aspx.vb", result);
    }

    [Fact]
    public void Process_MasterPageDirective_LanguageConverted()
    {
        const string input = "<%@ Master Language=\"VB\" CodeFile=\"Site.master.vb\" %>";
        var result = AspxMarkupProcessor.Process(input);
        Assert.Contains("Language=\"C#\"", result);
        Assert.Contains("Site.master.cs", result);
    }

    // ── If() ternary ─────────────────────────────────────────────────────────

    [Fact]
    public void Process_IfTernary_ConvertedToConditionalExpression()
    {
        const string input    = "<%# If(x, \"yes\", \"no\") %>";
        const string expected = "<%# (x ? \"yes\" : \"no\") %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_IfTernaryWithCBool_ConvertedCorrectly()
    {
        const string input    = "<%# If(CBool(Eval(\"Activo\")), \"Activo\", \"Inactivo\") %>";
        const string expected = "<%# (Convert.ToBoolean(Eval(\"Activo\")) ? \"Activo\" : \"Inactivo\") %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_IfTernaryWithStringEquality_ConvertedToDoubleEquals()
    {
        const string input    = "<%# If(Eval(\"Estado\").ToString() = \"Activo\", \"bg-success\", \"bg-secondary\") %>";
        const string expected = "<%# (Eval(\"Estado\").ToString() == \"Activo\" ? \"bg-success\" : \"bg-secondary\") %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_IfNullCoalescing_ConvertedToNotNullTernary()
    {
        const string input = "<%# If(Eval(\"Nombre\"), \"N/A\") %>";
        var result = AspxMarkupProcessor.Process(input);
        Assert.Contains("!=", result);
        Assert.Contains("N/A", result);
    }

    // ── Type coercions ───────────────────────────────────────────────────────

    [Fact]
    public void Process_CBool_ConvertedToConvertToBoolean()
    {
        const string input    = "<%# CBool(Eval(\"Activo\")) %>";
        const string expected = "<%# Convert.ToBoolean(Eval(\"Activo\")) %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_CInt_ConvertedToConvertToInt32()
    {
        const string input    = "<%# CInt(Eval(\"Id\")) %>";
        const string expected = "<%# Convert.ToInt32(Eval(\"Id\")) %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_CDec_ConvertedToConvertToDecimal()
    {
        const string input    = "<%# CDec(Eval(\"Precio\")) %>";
        const string expected = "<%# Convert.ToDecimal(Eval(\"Precio\")) %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    // ── Logical / comparison operators ───────────────────────────────────────

    [Fact]
    public void Process_NotOperator_ConvertedToExclamation()
    {
        const string input    = "<%# Not CBool(Eval(\"Completada\")) %>";
        const string expected = "<%# !Convert.ToBoolean(Eval(\"Completada\")) %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_InequalityOperator_ConvertedToNotEquals()
    {
        const string input    = "<%# Eval(\"Estado\") <> \"Inactivo\" %>";
        const string expected = "<%# Eval(\"Estado\") != \"Inactivo\" %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_StringConcatAmpersand_ConvertedToPlus()
    {
        const string input    = "<%# Eval(\"Nombre\") & \" \" & Eval(\"Apellido\") %>";
        const string expected = "<%# Eval(\"Nombre\") + \" \" + Eval(\"Apellido\") %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_AndAlso_ConvertedToDoubleAmpersand()
    {
        const string input    = "<%# x AndAlso y %>";
        const string expected = "<%# x && y %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_OrElse_ConvertedToDoublePipe()
    {
        const string input    = "<%# x OrElse y %>";
        const string expected = "<%# x || y %>";
        Assert.Equal(expected, AspxMarkupProcessor.Process(input));
    }

    // ── Skip unchanged ───────────────────────────────────────────────────────

    [Fact]
    public void Process_AlreadyCSharpMarkup_Unchanged()
    {
        const string input = "<%@ Page Language=\"C#\" CodeFile=\"Default.aspx.cs\" %>";
        Assert.Equal(input, AspxMarkupProcessor.Process(input));
    }

    [Fact]
    public void Process_NoVbMarkers_ReturnsIdentical()
    {
        const string input = "<asp:Label ID=\"lbl\" runat=\"server\" Text=\"Hello\" />";
        Assert.Equal(input, AspxMarkupProcessor.Process(input));
    }

    // ── Nested ternaries ─────────────────────────────────────────────────────

    [Fact]
    public void Process_NestedIfTernary_ConvertedCorrectly()
    {
        const string input = "<%# If(CBool(Eval(\"A\")), If(CBool(Eval(\"B\")), \"1\", \"2\"), \"3\") %>";
        var result = AspxMarkupProcessor.Process(input);
        Assert.Contains("?", result);
        Assert.DoesNotContain("If(", result);
    }
}
