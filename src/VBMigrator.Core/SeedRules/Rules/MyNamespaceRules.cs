using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.VisualBasic;
using Microsoft.CodeAnalysis.VisualBasic.Syntax;

namespace VBMigrator.Core.SeedRules.Rules;

/// <summary>
/// my_settings: My.Settings.Foo  →  Properties.Settings.Default.Foo
/// </summary>
public sealed class MySettingsRule : ISeedRule
{
    public string Tag => "my_settings";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not MemberAccessExpressionSyntax outer) return false;
        if (outer.Expression is not MemberAccessExpressionSyntax inner) return false;
        return MyHelper.IsMyIdentifier(inner.Expression) &&
               string.Equals(inner.Name.Identifier.Text, "Settings", StringComparison.Ordinal);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var outer = (MemberAccessExpressionSyntax)node;
        var propName = outer.Name.Identifier.Text;
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"Properties.Settings.Default.{propName}");
    }
}

/// <summary>
/// my_filesystem_read: My.Computer.FileSystem.ReadAllText(p)  →  System.IO.File.ReadAllText(p)
/// </summary>
public sealed class MyFilesystemReadRule : ISeedRule
{
    public string Tag => "my_filesystem_read";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        if (inv.Expression is not MemberAccessExpressionSyntax mem) return false;
        if (!string.Equals(mem.Name.Identifier.Text, "ReadAllText", StringComparison.Ordinal)) return false;
        return MyHelper.IsMyFileSystem(mem.Expression);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var inv  = (InvocationExpressionSyntax)node;
        var args = inv.ArgumentList?.ToString() ?? "()";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"System.IO.File.ReadAllText{args}");
    }
}

/// <summary>
/// my_filesystem_write: My.Computer.FileSystem.WriteAllText(p, s)  →  System.IO.File.WriteAllText(p, s)
/// </summary>
public sealed class MyFilesystemWriteRule : ISeedRule
{
    public string Tag => "my_filesystem_write";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not InvocationExpressionSyntax inv) return false;
        if (inv.Expression is not MemberAccessExpressionSyntax mem) return false;
        if (!string.Equals(mem.Name.Identifier.Text, "WriteAllText", StringComparison.Ordinal)) return false;
        return MyHelper.IsMyFileSystem(mem.Expression);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        var inv  = (InvocationExpressionSyntax)node;
        var args = inv.ArgumentList?.ToString() ?? "()";
        return Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression($"System.IO.File.WriteAllText{args}");
    }
}

/// <summary>
/// my_app_version: My.Application.Info.Version
///   →  Assembly.GetExecutingAssembly().GetName().Version
/// </summary>
public sealed class MyAppVersionRule : ISeedRule
{
    public string Tag => "my_app_version";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not MemberAccessExpressionSyntax outer) return false;
        if (!string.Equals(outer.Name.Identifier.Text, "Version", StringComparison.Ordinal)) return false;
        if (outer.Expression is not MemberAccessExpressionSyntax info) return false;
        if (!string.Equals(info.Name.Identifier.Text, "Info", StringComparison.Ordinal)) return false;
        if (info.Expression is not MemberAccessExpressionSyntax app) return false;
        return MyHelper.IsMyIdentifier(app.Expression) &&
               string.Equals(app.Name.Identifier.Text, "Application", StringComparison.Ordinal);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression("Assembly.GetExecutingAssembly().GetName().Version");
}

/// <summary>
/// my_user: My.User.Name  →  WindowsIdentity.GetCurrent().Name
/// </summary>
public sealed class MyUserRule : ISeedRule
{
    public string Tag => "my_user";
    public int Priority => 100;

    public bool CanHandle(SyntaxNode node, SemanticModel? semanticModel = null)
    {
        if (node is not MemberAccessExpressionSyntax outer) return false;
        if (!string.Equals(outer.Name.Identifier.Text, "Name", StringComparison.Ordinal)) return false;
        if (outer.Expression is not MemberAccessExpressionSyntax user) return false;
        return MyHelper.IsMyIdentifier(user.Expression) &&
               string.Equals(user.Name.Identifier.Text, "User", StringComparison.Ordinal);
    }

    public SyntaxNode Convert(SyntaxNode node, SemanticModel? semanticModel = null)
        => Microsoft.CodeAnalysis.CSharp.SyntaxFactory
            .ParseExpression("WindowsIdentity.GetCurrent().Name");
}

file static class MyHelper
{
    internal static bool IsMyIdentifier(SyntaxNode node)
        => node is IdentifierNameSyntax id &&
           string.Equals(id.Identifier.Text, "My", StringComparison.Ordinal);

    internal static bool IsMyFileSystem(SyntaxNode node)
    {
        if (node is not MemberAccessExpressionSyntax fs) return false;
        if (!string.Equals(fs.Name.Identifier.Text, "FileSystem", StringComparison.Ordinal)) return false;
        if (fs.Expression is not MemberAccessExpressionSyntax computer) return false;
        return IsMyIdentifier(computer.Expression) &&
               string.Equals(computer.Name.Identifier.Text, "Computer", StringComparison.Ordinal);
    }
}
