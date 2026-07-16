using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VBMigrator.VSIX.Services;

public class CliRunner
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // For --file: returns single JSON result
    public async Task<TranslationResultDto?> ConvertFileAsync(
        string filePath, CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = CliLocator.FindExecutable(),
            Arguments              = $"convert --file \"{filePath}\" --json-output",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var proc = Process.Start(psi)!;

        // Drain stdout + stderr concurrently — prevents deadlock on large files
        var stdoutTask  = proc.StandardOutput.ReadToEndAsync();
        var stderrTask  = proc.StandardError.ReadToEndAsync();
        await Task.Run(() => proc.WaitForExit(), ct);
        var json    = await stdoutTask;
        var _errors = await stderrTask;  // never ignore the Task

        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<TranslationResultDto>(json, JsonOpts);
    }

    // For --solution: fires and monitors stderr for progress, returns exit code
    public async Task<int> ConvertSolutionAsync(
        string slnPath, string? outputDir,
        Action<string>? onProgress, CancellationToken ct = default)
    {
        var args = $"convert --solution \"{slnPath}\"";
        if (outputDir != null) args += $" --output \"{outputDir}\"";

        var psi = new ProcessStartInfo
        {
            FileName               = CliLocator.FindExecutable(),
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };

        using var proc = Process.Start(psi)!;

        // Read stderr line-by-line for progress (FILE: name.vb OK|FAIL|HUMAN_QUEUE)
        proc.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null) onProgress?.Invoke(e.Data);
        };
        proc.BeginErrorReadLine();

        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        await Task.Run(() => proc.WaitForExit(), ct);
        await stdoutTask;

        return proc.ExitCode;
    }
}
