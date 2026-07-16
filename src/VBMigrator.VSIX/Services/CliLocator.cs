using System;
using System.IO;

namespace VBMigrator.VSIX.Services;

public static class CliLocator
{
    private static string? _configuredPath;

    public static void SetConfiguredPath(string? path) => _configuredPath = path;

    public static string FindExecutable()
    {
        // (1) configured via Tools→VBMigrator→Settings
        if (!string.IsNullOrEmpty(_configuredPath) && File.Exists(_configuredPath))
            return _configuredPath;

        // (2) PATH
        var inPath = FindOnPath("vbmigrator.exe") ?? FindOnPath("vbmigrator");
        if (inPath != null)
            return inPath;

        // (3) Error
        throw new InvalidOperationException(
            "vbmigrator CLI not found. Install it with: dotnet tool install -g VBMigrator.CLI\n" +
            "Or set the path in Tools → VBMigrator → Settings.");
    }

    private static string? FindOnPath(string exe)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir.Trim(), exe);
            if (File.Exists(full)) return full;
        }
        return null;
    }
}
