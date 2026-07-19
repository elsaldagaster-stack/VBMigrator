using System.Text.RegularExpressions;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Regex-based post-processor for known ICSharpCode output gaps.
/// Runs on assembled C# after ReassembleFile, before validation.
/// </summary>
public static class CsOutputFixer
{
    // VB default-property indexers that ICSharpCode leaves as () instead of []
    private static readonly Regex _vbIndexerParens = new(
        @"\b(ConnectionStrings|AppSettings|Session|ViewState|Cache|Application|Form|QueryString|ServerVariables|Cookies|Headers)\s*\(\s*""([^""]+)""\s*\)",
        RegexOptions.Compiled);

    // VB ParamArray T() → C# params T[]  (e.g. params SqlParameter())
    private static readonly Regex _paramArrayBrackets = new(
        @"\bparams\s+(\w+(?:<[^>]+>)?)\s*\(\)",
        RegexOptions.Compiled);

    // C# 9 negation pattern → Framework-compatible null check
    private static readonly Regex _isNotNull = new(
        @"\bis\s+not\s+null\b",
        RegexOptions.Compiled);

    // C# 9 is null pattern → explicit == null (safe for Framework 4.8)
    private static readonly Regex _isNull = new(
        @"\bis\s+null\b",
        RegexOptions.Compiled);

    // VB Friend → C# internal — ASP.NET code-behind partial classes must be public
    private static readonly Regex _internalPartialClass = new(
        @"\binternal\s+partial\s+class\b",
        RegexOptions.Compiled);

    // VB Global:: namespace qualifier — invalid in C#, strip it
    private static readonly Regex _globalQualifier = new(
        @"\bGlobal\.",
        RegexOptions.Compiled);

    // VB Chr() function — ICSharpCode leaves Chr[N] or Chr(N), convert to char literal
    private static readonly Regex _chrFunction = new(
        @"\bChr[\[(](\d+)[\])]",
        RegexOptions.Compiled);

    // C# 7.1 default literal — aspnet_compiler only supports up to C# 7.0 in Framework 4.8
    // "Type? var = default;" → "Type? var = default(Type?);"
    private static readonly Regex _defaultLiteral = new(
        @"\b([A-Za-z]\w*\??)\s+(\w+)\s*=\s*default\s*;",
        RegexOptions.Compiled);

    // Object initializer self-assignment from VB case insensitivity: "precio = precio," → "Precio = precio,"
    // Only matches whole-line initializer members: "    propName = propName,"
    private static readonly Regex _selfAssignInitializer = new(
        @"^([ \t]*)([a-z])(\w+)(\s*=\s*)\2\3(\s*,?\s*)$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    // VB case-insensitive class/variable collision: "usuario usuario = usuario.Static()" → use type name
    // Matches: TypeName varName = varName.  where TypeName and varName differ only by case
    private static readonly Regex _caseInsensitiveStaticCall = new(
        @"\b([A-Z]\w+)\s+([a-z]\w+)\s*=\s*\2\.",
        RegexOptions.Compiled);

    // ASP.NET control fields auto-generated from markup — duplicates in code-behind cause CS0102
    // Matches: protected System.Web.UI.WebControls.Label myLabel;
    private static readonly Regex _aspNetControlField = new(
        @"^[ \t]*protected\s+System\.Web\.UI\.(WebControls|HtmlControls)\.\w+\s+\w+\s*;\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static string Fix(string csSource)
    {
        csSource = _vbIndexerParens.Replace(csSource, @"$1[""$2""]");
        csSource = _paramArrayBrackets.Replace(csSource, "params $1[]");
        csSource = _isNotNull.Replace(csSource, "!= null");
        csSource = _isNull.Replace(csSource, "== null");
        csSource = _internalPartialClass.Replace(csSource, "public partial class");
        csSource = _globalQualifier.Replace(csSource, string.Empty);
        csSource = _aspNetControlField.Replace(csSource, string.Empty);
        csSource = _chrFunction.Replace(csSource, m =>
        {
            int code = int.Parse(m.Groups[1].Value);
            // VB Chr() returns String, not Char — use string literal to preserve Replace(string,string) overload
            return code == 34 ? "\"\\\"\"" : $"((char){code}).ToString()";
        });
        csSource = _defaultLiteral.Replace(csSource, m =>
            $"{m.Groups[1].Value} {m.Groups[2].Value} = default({m.Groups[1].Value});");
        csSource = _selfAssignInitializer.Replace(csSource, m =>
        {
            var indent  = m.Groups[1].Value;
            var upper   = char.ToUpper(m.Groups[2].Value[0]);
            var rest    = m.Groups[3].Value;
            var assign  = m.Groups[4].Value;
            var lower   = m.Groups[2].Value;
            var trail   = m.Groups[5].Value;
            return $"{indent}{upper}{rest}{assign}{lower}{rest}{trail}";
        });
        csSource = _caseInsensitiveStaticCall.Replace(csSource, m =>
        {
            var typeName = m.Groups[1].Value;
            var varName  = m.Groups[2].Value;
            if (string.Equals(typeName, varName, StringComparison.OrdinalIgnoreCase))
                return m.Value.Replace(varName + ".", typeName + ".");
            return m.Value;
        });
        return csSource;
    }
}
