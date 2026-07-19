using System.Text;
using System.Text.RegularExpressions;

namespace VBMigrator.Core.Translator;

/// <summary>
/// Transforms VB-specific markup in .aspx/.master/.ascx files to C# equivalents.
/// Handles directive attributes and <%# %> / <% %> inline expression blocks.
/// </summary>
public static class AspxMarkupProcessor
{
    // Language="VB" → Language="C#" in <%@ ... %> directives
    private static readonly Regex _directiveLanguage = new(
        @"Language\s*=\s*""VB""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // CodeFile="*.vb" or CodeBehind="*.vb" → *.cs
    private static readonly Regex _codeFileVb = new(
        @"(Code(?:File|Behind)\s*=\s*"")([^""]+?)\.vb("")",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // <%# ... %> data-binding expression blocks
    private static readonly Regex _dataBindBlock = new(
        @"<%#(.*?)%>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // <% ... %> server-side code blocks (not directives <%@ or expressions <%=)
    private static readonly Regex _codeBlock = new(
        @"<%(?![@#=])(.*?)%>",
        RegexOptions.Singleline | RegexOptions.Compiled);

    // VB conversion patterns applied inside expression blocks
    private static readonly Regex _cBool  = new(@"\bCBool\(",  RegexOptions.Compiled);
    private static readonly Regex _cInt   = new(@"\bCInt\(",   RegexOptions.Compiled);
    private static readonly Regex _cDec   = new(@"\bCDec\(",   RegexOptions.Compiled);
    private static readonly Regex _cStr   = new(@"\bCStr\(",   RegexOptions.Compiled);
    private static readonly Regex _notOp  = new(@"\bNot\s+",   RegexOptions.Compiled);
    private static readonly Regex _andAlso = new(@"\bAndAlso\b", RegexOptions.Compiled);
    private static readonly Regex _orElse  = new(@"\bOrElse\b",  RegexOptions.Compiled);

    public static string Process(string markup)
    {
        markup = _directiveLanguage.Replace(markup, @"Language=""C#""");
        markup = _codeFileVb.Replace(markup, "$1$2.cs$3");

        markup = _dataBindBlock.Replace(markup, m =>
            "<%#" + ConvertVbExpression(m.Groups[1].Value) + "%>");

        markup = _codeBlock.Replace(markup, m =>
            "<%" + ConvertVbExpression(m.Groups[1].Value) + "%>");

        return markup;
    }

    private static string ConvertVbExpression(string expr)
    {
        expr = ConvertIfTernary(expr);
        expr = _cBool.Replace(expr,   "Convert.ToBoolean(");
        expr = _cInt.Replace(expr,    "Convert.ToInt32(");
        expr = _cDec.Replace(expr,    "Convert.ToDecimal(");
        expr = _cStr.Replace(expr,    "(");          // CStr(x) → (x) then .ToString() would need more context; leave as cast-free call
        expr = _notOp.Replace(expr,   "!");
        expr = _andAlso.Replace(expr, "&&");
        expr = _orElse.Replace(expr,  "||");
        expr = expr.Replace("<>", "!=");
        // VB string concat &  →  + (only " & " with spaces to avoid breaking HTML &amp; etc.)
        expr = Regex.Replace(expr, @" & ", " + ");
        return expr;
    }

    // Converts VB If(cond, trueVal, falseVal) → (cond ? trueVal : falseVal)
    // Handles nested function calls via balanced-paren scanning.
    private static string ConvertIfTernary(string expr)
    {
        var sb  = new StringBuilder(expr.Length);
        int pos = 0;

        while (pos < expr.Length)
        {
            int ifIdx = FindIf(expr, pos);
            if (ifIdx < 0)
            {
                sb.Append(expr, pos, expr.Length - pos);
                break;
            }

            sb.Append(expr, pos, ifIdx - pos);

            int parenOpen = ifIdx + 2;
            if (parenOpen >= expr.Length || expr[parenOpen] != '(')
            {
                sb.Append("If");
                pos = ifIdx + 2;
                continue;
            }

            var parsed = SplitBalancedArgs(expr, parenOpen);
            if (parsed == null || (parsed.Value.Args.Count != 2 && parsed.Value.Args.Count != 3))
            {
                int end = parsed?.End ?? (parenOpen + 1);
                sb.Append(expr, ifIdx, end - ifIdx);
                pos = end;
                continue;
            }

            var (args, argEnd) = parsed.Value;

            if (args.Count == 2)
            {
                // If(expr, defaultVal) — VB null-coalescing; emit as ternary with != null check
                var a0 = ConvertVbExpression(args[0].Trim());
                var a1 = ConvertVbExpression(args[1].Trim());
                sb.Append($"({a0} != null ? {a0} : {a1})");
            }
            else
            {
                var cond = ConvertVbExpression(args[0].Trim());
                var t    = ConvertVbExpression(args[1].Trim());
                var f    = ConvertVbExpression(args[2].Trim());
                sb.Append($"({cond} ? {t} : {f})");
            }
            pos = argEnd;
        }

        return sb.ToString();
    }

    // Finds the next standalone "If(" in s starting at pos.
    // Standalone = not preceded by a letter, digit, or underscore.
    private static int FindIf(string s, int start)
    {
        int i = start;
        while (i <= s.Length - 3)
        {
            if (s[i] == 'I' && s[i + 1] == 'f' && s[i + 2] == '(')
            {
                if (i == 0 || !(char.IsLetterOrDigit(s[i - 1]) || s[i - 1] == '_'))
                    return i;
            }
            i++;
        }
        return -1;
    }

    // Scans from the opening '(' at parenStart, splitting top-level commas into args.
    private static (List<string> Args, int End)? SplitBalancedArgs(string s, int parenStart)
    {
        var  args     = new List<string>();
        int  depth    = 0;
        int  argStart = parenStart + 1;
        bool inString = false;

        for (int i = parenStart; i < s.Length; i++)
        {
            char c = s[i];

            if (inString)
            {
                if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    break;
                case '(':
                case '[':
                    depth++;
                    break;
                case ')':
                case ']':
                    depth--;
                    if (depth == 0)
                    {
                        args.Add(s[argStart..i]);
                        return (args, i + 1);
                    }
                    break;
                case ',' when depth == 1:
                    args.Add(s[argStart..i]);
                    argStart = i + 1;
                    break;
            }
        }

        return null; // unbalanced
    }
}
