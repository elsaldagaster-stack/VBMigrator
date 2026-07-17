using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VBMigrator.VSIX.Services;

namespace VBMigrator.VSIX.Commands;

public sealed class ConvertSolutionCommand
{
    public const int CommandId        = 0x0102;
    public const int CommandIdProject = 0x0103;
    public static readonly Guid CommandSet = ConvertFileCommand.CommandSet;

    private readonly AsyncPackage _package;

    private ConvertSolutionCommand(AsyncPackage package, OleMenuCommandService svc)
    {
        _package = package;
        svc.AddCommand(new OleMenuCommand(Execute, new CommandID(CommandSet, CommandId)));
        svc.AddCommand(new OleMenuCommand(Execute, new CommandID(CommandSet, CommandIdProject)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var svc = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (svc != null) new ConvertSolutionCommand(package, svc);
    }

    private async void Execute(object sender, EventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var slnPath = dte?.Solution?.FullName;

            // Solution.FullName is empty when project opened directly (no .sln loaded).
            // Walk up from the first loaded project's directory to find a .sln file.
            if (string.IsNullOrEmpty(slnPath))
            {
                var projects = dte?.Solution?.Projects;
                if (projects != null && projects.Count > 0)
                {
                    var projFile = projects.Item(1)?.FullName;
                    var dir = string.IsNullOrEmpty(projFile)
                        ? null
                        : new DirectoryInfo(Path.GetDirectoryName(projFile)!);
                    while (dir != null && string.IsNullOrEmpty(slnPath))
                    {
                        var found = dir.GetFiles("*.sln").FirstOrDefault();
                        if (found != null) slnPath = found.FullName;
                        dir = dir.Parent;
                    }
                }
            }

            if (string.IsNullOrEmpty(slnPath))
            {
                System.Windows.MessageBox.Show(
                    "No .sln file found. Save the solution (File → Save All) and try again.",
                    "VBMigrator",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (dte?.StatusBar != null)
                dte.StatusBar.Text = "VBMigrator: converting solution…";

            var progressLog = new System.Text.StringBuilder();
            var runner = new CliRunner();
            var exitCode = await runner.ConvertSolutionAsync(
                slnPath, outputDir: null,
                onProgress: line =>
                {
                    progressLog.AppendLine(line);
                    System.Diagnostics.Debug.WriteLine($"[VBMigrator] {line}");
                });

            if (dte?.StatusBar != null)
                dte.StatusBar.Text = exitCode == 0
                    ? "VBMigrator: conversion complete."
                    : "VBMigrator: conversion failed.";

            if (exitCode != 0)
            {
                System.Windows.MessageBox.Show(
                    $"VBMigrator: CLI exited with code {exitCode}.\n\n{progressLog}",
                    "VBMigrator",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return;
            }

            await _package.ShowToolWindowAsync(
                typeof(ToolWindows.ReviewQueueWindow), 0, true, _package.DisposalToken);
        }
        catch (Exception ex)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            System.Windows.MessageBox.Show(
                $"VBMigrator error: {ex.Message}",
                "VBMigrator",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
    }
}
