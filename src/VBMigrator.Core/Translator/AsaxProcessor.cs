using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Translates global.asax (and similar .asax files) from VB to C#.
/// Wraps the script block content in a dummy VB class, runs it through
/// ICSharpCode, then unwraps and reconstructs the .asax file.
/// </summary>
public static class AsaxProcessor
{
    private static readonly Regex _directiveLang = new(
        @"Language\s*=\s*""VB""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex _scriptBlock = new(
        @"(<script\b[^>]*\brunat\s*=\s*""server""[^>]*>)(.*?)(</script>)",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Returns the processed .asax content, or the original if no VB markers found.
    /// </summary>
    public static async Task<string> ProcessAsync(string asaxContent, RoslynTranslator translator)
    {
        if (!_directiveLang.IsMatch(asaxContent))
            return asaxContent;

        // Fix directive attribute
        asaxContent = _directiveLang.Replace(asaxContent, @"Language=""C#""");

        // Translate script block content
        var scriptMatch = _scriptBlock.Match(asaxContent);
        if (!scriptMatch.Success)
            return asaxContent;

        var openTag      = scriptMatch.Groups[1].Value;
        var vbBody       = scriptMatch.Groups[2].Value;
        var closeTag     = scriptMatch.Groups[3].Value;
        var translatedCs = await TranslateScriptBlockAsync(vbBody, translator);

        var before = asaxContent[..scriptMatch.Index];
        var after  = asaxContent[(scriptMatch.Index + scriptMatch.Length)..];
        return before + openTag + translatedCs + closeTag + after;
    }

    private static async Task<string> TranslateScriptBlockAsync(string vbBody, RoslynTranslator translator)
    {
        // Wrap in minimal VB class so ICSharpCode can parse method declarations
        var vbWrapped = string.Join("\r\n",
            "Imports System",
            "Imports System.Web",
            "Imports System.Web.Security",
            "Imports System.Security.Principal",
            "Imports System.Diagnostics",
            "",
            "Public Class _AsaxEvents",
            vbBody,
            "End Class");

        var result = await translator.ConvertFileAsync(vbWrapped, "global.asax");

        if (!result.Success || string.IsNullOrWhiteSpace(result.CsSource))
            return vbBody; // fallback: leave VB in place

        // Apply same post-fixes as the main pipeline
        var fixedCs = CsOutputFixer.Fix(result.CsSource);

        // Extract member bodies from the generated class using Roslyn
        var csTree   = CSharpSyntaxTree.ParseText(fixedCs);
        var typeDecl = csTree.GetRoot()
            .DescendantNodes()
            .OfType<TypeDeclarationSyntax>()
            .FirstOrDefault();

        if (typeDecl == null)
            return fixedCs;

        // Emit each member indented inside the script block
        var sb = new StringBuilder();
        sb.AppendLine();
        foreach (var member in typeDecl.Members)
        {
            var text = member.ToFullString().Trim('\r', '\n', ' ');
            foreach (var line in text.Split('\n'))
                sb.AppendLine("    " + line.TrimEnd('\r'));
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
