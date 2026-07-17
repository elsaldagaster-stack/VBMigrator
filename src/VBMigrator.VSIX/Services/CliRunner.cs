using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VBMigrator.VSIX.Services;

public class CliRunner
{
    private readonly string? _apiKey;

    public CliRunner(string? apiKey = null) { _apiKey = apiKey; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private ProcessStartInfo MakePsi(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = CliLocator.FindExecutable(),
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        if (!string.IsNullOrEmpty(_apiKey))
            psi.EnvironmentVariables["ANTHROPIC_API_KEY"] = _apiKey;
        return psi;
    }

    // For --file: returns single JSON result
    public async Task<TranslationResultDto?> ConvertFileAsync(
        string filePath, CancellationToken ct = default)
    {
        var psi = MakePsi($"convert --file \"{filePath}\" --json-output");

        using var proc = Process.Start(psi)!;

        // Drain stdout + stderr concurrently — prevents deadlock on large files
        var stdoutTask  = proc.StandardOutput.ReadToEndAsync();
        var stderrTask  = proc.StandardError.ReadToEndAsync();
        await Task.Run(() => proc.WaitForExit(), ct);
        var json    = await stdoutTask;
        var _errors = await stderrTask;

        if (string.IsNullOrWhiteSpace(json)) return null;
        return JsonSerializer.Deserialize<TranslationResultDto>(json, JsonOpts);
    }

    // For --solution: fires and monitors stderr for progress, returns exit code
    public async Task<int> ConvertSolutionAsync(
        string slnPath, string? outputDir,
        Action<string>? onProgress,
        bool replace = false, string? backupDir = null,
        CancellationToken ct = default)
    {
        var args = $"convert --solution \"{slnPath}\"";
        if (outputDir  != null) args += $" --output \"{outputDir}\"";
        if (replace)            args += " --replace";
        if (backupDir  != null) args += $" --backup-dir \"{backupDir}\"";

        var psi  = MakePsi(args);
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

    // For --folder: fires and monitors stderr for progress, returns exit code
    public async Task<int> ConvertFolderAsync(
        string folderPath, string? outputDir,
        Action<string>? onProgress,
        bool replace = false, string? backupDir = null,
        CancellationToken ct = default)
    {
        var args = $"convert --folder \"{folderPath}\"";
        if (outputDir  != null) args += $" --output \"{outputDir}\"";
        if (replace)            args += " --replace";
        if (backupDir  != null) args += $" --backup-dir \"{backupDir}\"";

        var psi  = MakePsi(args);
        using var proc = Process.Start(psi)!;

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

    public async Task<List<ReviewQueueItem>> GetQueueAsync()
    {
        var psi = MakePsi("queue list");
        using var proc = Process.Start(psi)!;
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        await Task.Run(() => proc.WaitForExit());
        var json = await stdoutTask;
        var _err = await stderrTask;
        if (string.IsNullOrWhiteSpace(json)) return new List<ReviewQueueItem>();
        return JsonSerializer.Deserialize<List<ReviewQueueItem>>(json, JsonOpts)
               ?? new List<ReviewQueueItem>();
    }

    public async Task AcceptQueueItemAsync(int id)
    {
        var psi = MakePsi($"queue accept --id {id}");
        using var proc = Process.Start(psi)!;
        await Task.Run(() => proc.WaitForExit());
    }

    public async Task DismissQueueItemAsync(int id)
    {
        var psi = MakePsi($"queue dismiss --id {id}");
        using var proc = Process.Start(psi)!;
        await Task.Run(() => proc.WaitForExit());
    }
}
