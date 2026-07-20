using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class PropertyCaseFixerTests
{
    private const string ModelSource = """
        public class Cotizacion
        {
            public decimal Subtotal { get; set; }
            public decimal IGV { get; set; }
            public decimal Total { get; set; }
            public string Estado { get; set; }
        }

        public class CotizacionDetalle
        {
            public int CotizacionId { get; set; }
            public decimal Subtotal { get; set; }
            public decimal PrecioUnitario { get; set; }
        }
        """;

    [Fact]
    public void BuildMemberIndex_ExtractsPropertyNames()
    {
        var index = PropertyCaseFixer.BuildMemberIndex([ModelSource]);
        Assert.True(index.ContainsKey("Cotizacion"));
        Assert.Contains("IGV", index["Cotizacion"]);
        Assert.Contains("Subtotal", index["Cotizacion"]);
    }

    [Fact]
    public void Fix_ObjectInitializerCaseMismatch_Corrected()
    {
        var index = PropertyCaseFixer.BuildMemberIndex([ModelSource]);
        const string input = """
            var co = new Cotizacion()
            {
                Subtotal = subtotal,
                Igv = igv,
                Total = total
            };
            """;

        var result = PropertyCaseFixer.Fix(input, index);

        Assert.Contains("IGV = igv", result);
        Assert.DoesNotContain("Igv = igv", result);
    }

    [Fact]
    public void Fix_AlreadyCorrectCase_Unchanged()
    {
        var index = PropertyCaseFixer.BuildMemberIndex([ModelSource]);
        const string input = """
            var co = new Cotizacion()
            {
                Subtotal = subtotal,
                IGV = igv,
                Total = total
            };
            """;

        var result = PropertyCaseFixer.Fix(input, index);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Fix_CotizacionDetalleSubtotal_Corrected()
    {
        var index = PropertyCaseFixer.BuildMemberIndex([ModelSource]);
        const string input = """
            var d = new CotizacionDetalle()
            {
                CotizacionId = id,
                subtotal = Convert.ToDecimal(fila["Subtotal"])
            };
            """;

        var result = PropertyCaseFixer.Fix(input, index);
        Assert.Contains("Subtotal = Convert.ToDecimal", result);
        Assert.DoesNotContain("subtotal = Convert.ToDecimal", result);
    }

    [Fact]
    public void Fix_UnknownType_NotModified()
    {
        var index = PropertyCaseFixer.BuildMemberIndex([ModelSource]);
        const string input = """
            var x = new UnknownType() { someField = 1 };
            """;

        var result = PropertyCaseFixer.Fix(input, index);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Fix_EmptyIndex_ReturnsOriginal()
    {
        var index = new Dictionary<string, HashSet<string>>();
        const string input = "var x = new Foo() { bar = 1 };";
        Assert.Equal(input, PropertyCaseFixer.Fix(input, index));
    }

    [Fact]
    public void Fix_NestedObjectCreation_BothFixed()
    {
        var index = PropertyCaseFixer.BuildMemberIndex([ModelSource]);
        const string input = """
            var co = new Cotizacion()
            {
                IGV = igv,
                Subtotal = new CotizacionDetalle() { subtotal = 10m }.Subtotal
            };
            """;

        var result = PropertyCaseFixer.Fix(input, index);
        // Inner CotizacionDetalle.subtotal should be fixed
        Assert.DoesNotContain("{ subtotal = 10m }", result);
        Assert.Contains("{ Subtotal = 10m }", result);
    }
}
