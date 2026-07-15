using System.Text.RegularExpressions;

namespace VBMigrator.Core.AspxHandler;

public static class AspxDirectiveRewriter
{
    public static string Rewrite(string aspxContent)
    {
        var result = Regex.Replace(aspxContent,
            @"Language\s*=\s*""VB""",
            @"Language=""C#""",
            RegexOptions.IgnoreCase);

        result = Regex.Replace(result,
            @"CodeBehind\s*=\s*""([^""]+)\.aspx\.vb""",
            m => $@"CodeBehind=""{m.Groups[1].Value}.aspx.cs""",
            RegexOptions.IgnoreCase);

        return result;
    }
}
