using VBMigrator.Core.Translator;
using Xunit;

namespace VBMigrator.Core.Tests.Translator;

public class LlmUsingResolverTests
{
    private readonly LlmUsingResolver _resolver = new();

    [Fact]
    public void Resolve_WindowsIdentity_ReturnsSecurityPrincipalNamespace()
    {
        var result = _resolver.Resolve("var name = WindowsIdentity.GetCurrent().Name;");
        Assert.Contains("System.Security.Principal", result.Namespaces);
    }

    [Fact]
    public void Resolve_Regex_ReturnsRegularExpressionsNamespace()
    {
        var result = _resolver.Resolve("var ok = Regex.IsMatch(s, @\"^A.*$\");");
        Assert.Contains("System.Text.RegularExpressions", result.Namespaces);
    }

    [Fact]
    public void Resolve_Assembly_ReturnsReflectionNamespace()
    {
        var result = _resolver.Resolve("var ver = Assembly.GetExecutingAssembly().GetName().Version;");
        Assert.Contains("System.Reflection", result.Namespaces);
    }

    [Fact]
    public void Resolve_DateTime_ReturnsSystemNamespace()
    {
        var result = _resolver.Resolve("var d = new DateTime(2020, 1, 1);");
        Assert.Contains("System", result.Namespaces);
    }

    [Fact]
    public void Resolve_NoKnownTypes_ReturnsEmptyNamespaces()
    {
        var result = _resolver.Resolve("var x = 1 + 2;");
        Assert.DoesNotContain("System.Reflection", result.Namespaces);
        Assert.DoesNotContain("System.Security.Principal", result.Namespaces);
    }

    [Fact]
    public void ToUsingDirectives_ProducesCorrectSyntax()
    {
        var directives = LlmUsingResolver.ToUsingDirectives(new[] { "System.IO", "System.Text" }).ToList();
        Assert.Contains("using System.IO;", directives);
        Assert.Contains("using System.Text;", directives);
    }

    [Fact]
    public void WellKnown_ContainsAllSeedRuleIntroducedTypes()
    {
        var required = new[]
        {
            "WindowsIdentity", "Regex", "Assembly",
            "DateTime", "StringComparison", "Math"
        };
        foreach (var type in required)
            Assert.True(LlmUsingResolver.WellKnown.ContainsKey(type),
                $"WellKnown table missing entry for '{type}'");
    }

    [Fact]
    public void Resolve_MultipleKnownTypes_ReturnsAllNamespaces()
    {
        var csMethod = @"
var name = WindowsIdentity.GetCurrent().Name;
var ok   = Regex.IsMatch(name, @""^A"");
var d    = new DateTime(2020, 1, 1);";
        var result = _resolver.Resolve(csMethod);
        Assert.Contains("System.Security.Principal", result.Namespaces);
        Assert.Contains("System.Text.RegularExpressions", result.Namespaces);
        Assert.Contains("System", result.Namespaces);
    }
}
