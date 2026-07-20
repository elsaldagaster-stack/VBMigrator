using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class CsOutputFixerTests
{
    // ── VB indexer parens ────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"Session(""key"")",            @"Session[""key""]")]
    [InlineData(@"ViewState(""x"")",            @"ViewState[""x""]")]
    [InlineData(@"Application(""counter"")",    @"Application[""counter""]")]
    [InlineData(@"QueryString(""id"")",         @"QueryString[""id""]")]
    [InlineData(@"Form(""field"")",             @"Form[""field""]")]
    [InlineData(@"ConnectionStrings(""conn"")", @"ConnectionStrings[""conn""]")]
    [InlineData(@"AppSettings(""key"")",        @"AppSettings[""key""]")]
    public void Fix_VbIndexerParens_ConvertedToBrackets(string input, string expected)
        => Assert.Equal(expected, CsOutputFixer.Fix(input));

    // ── ParamArray brackets ──────────────────────────────────────────────────

    [Fact]
    public void Fix_ParamArrayParens_ConvertedToBrackets()
        => Assert.Equal("params SqlParameter[]", CsOutputFixer.Fix("params SqlParameter()"));

    [Fact]
    public void Fix_ParamArrayGenericParens_ConvertedToBrackets()
        => Assert.Equal("params List<string>[]", CsOutputFixer.Fix("params List<string>()"));

    // ── is not null / is null ────────────────────────────────────────────────

    [Fact]
    public void Fix_IsNotNull_ConvertedToNotEqualsNull()
        => Assert.Equal("if (x != null)", CsOutputFixer.Fix("if (x is not null)"));

    [Fact]
    public void Fix_IsNull_ConvertedToEqualsNull()
        => Assert.Equal("if (x == null)", CsOutputFixer.Fix("if (x is null)"));

    // ── internal partial class ───────────────────────────────────────────────

    [Fact]
    public void Fix_InternalPartialClass_ConvertedToPublic()
        => Assert.Equal("public partial class MyPage", CsOutputFixer.Fix("internal partial class MyPage"));

    // ── Global. qualifier ────────────────────────────────────────────────────

    [Fact]
    public void Fix_GlobalQualifier_Stripped()
        => Assert.Equal("System.Web.UI.WebControls.Label", CsOutputFixer.Fix("Global.System.Web.UI.WebControls.Label"));

    // ── ASP.NET control field removal ────────────────────────────────────────

    [Fact]
    public void Fix_AspNetControlField_Removed()
    {
        const string input = "    protected System.Web.UI.WebControls.Label lblName;\r\n";
        var result = CsOutputFixer.Fix(input);
        Assert.DoesNotContain("protected System.Web.UI.WebControls.Label", result);
    }

    [Fact]
    public void Fix_AspNetHtmlControlField_Removed()
    {
        const string input = "    protected System.Web.UI.HtmlControls.HtmlForm form1;\r\n";
        var result = CsOutputFixer.Fix(input);
        Assert.DoesNotContain("protected System.Web.UI.HtmlControls.HtmlForm", result);
    }

    [Fact]
    public void Fix_RegularProtectedField_NotRemoved()
    {
        const string input = "    protected string _name;\r\n";
        Assert.Equal(input, CsOutputFixer.Fix(input));
    }

    // ── Chr() function ───────────────────────────────────────────────────────

    [Fact]
    public void Fix_Chr34_ConvertedToEscapedStringLiteral()
        => Assert.Equal("\"\\\"\"", CsOutputFixer.Fix("Chr(34)"));

    [Fact]
    public void Fix_ChrWithBrackets_ConvertedToCharToString()
        => Assert.Equal("((char)65).ToString()", CsOutputFixer.Fix("Chr[65]"));

    [Fact]
    public void Fix_ChrNon34_ConvertedToCharToString()
        => Assert.Equal("((char)124).ToString()", CsOutputFixer.Fix("Chr(124)"));

    // ── default literal ──────────────────────────────────────────────────────

    [Fact]
    public void Fix_DefaultLiteral_ExpandedToDefaultExpression()
    {
        const string input    = "string? s = default;";
        const string expected = "string? s = default(string?);";
        Assert.Equal(expected, CsOutputFixer.Fix(input));
    }

    [Fact]
    public void Fix_DefaultLiteralNonNullable_ExpandedToDefaultExpression()
    {
        const string input    = "int count = default;";
        const string expected = "int count = default(int);";
        Assert.Equal(expected, CsOutputFixer.Fix(input));
    }

    // ── self-assign initializer ──────────────────────────────────────────────

    [Fact]
    public void Fix_SelfAssignInitializer_UppercasesPropertyName()
    {
        const string input    = "    precio = precio,";
        const string expected = "    Precio = precio,";
        Assert.Equal(expected, CsOutputFixer.Fix(input));
    }

    [Fact]
    public void Fix_SelfAssignInitializerNoTrailingComma_UppercasesPropertyName()
    {
        const string input    = "    nombre = nombre";
        const string expected = "    Nombre = nombre";
        Assert.Equal(expected, CsOutputFixer.Fix(input));
    }

    // ── case-insensitive static call ─────────────────────────────────────────

    [Fact]
    public void Fix_CaseInsensitiveStaticCall_UsesTypeName()
    {
        const string input    = "Usuario usuario = usuario.ValidarCredenciales(user, pass);";
        const string expected = "Usuario usuario = Usuario.ValidarCredenciales(user, pass);";
        Assert.Equal(expected, CsOutputFixer.Fix(input));
    }

    [Fact]
    public void Fix_CaseInsensitiveStaticCall_DifferentNamesNotAffected()
    {
        const string input = "Producto item = item.Clone();";
        Assert.Equal(input, CsOutputFixer.Fix(input));
    }

    // ── idempotency ──────────────────────────────────────────────────────────

    [Fact]
    public void Fix_AlreadyValidCSharp_Unchanged()
    {
        const string input = "var x = Session[\"key\"] ?? string.Empty;";
        Assert.Equal(input, CsOutputFixer.Fix(input));
    }
}
