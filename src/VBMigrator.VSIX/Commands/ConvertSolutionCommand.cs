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

            // Ask whether to replace source files
            var replaceChoice = System.Windows.MessageBox.Show(
                "Replace source files after conversion?\n\n" +
                "  • .vb files → deleted (replaced by .cs)\n" +
                "  • .vbproj  → removed from solution and deleted\n" +
                "  • .csproj  → added to solution\n\n" +
                "Yes    = replace and create backup\n" +
                "No     = replace without backup\n" +
                "Cancel = convert only, keep .vb files",
                "VBMigrator — Replace files?",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question);

            if (replaceChoice == System.Windows.MessageBoxResult.Cancel)
                return;

            bool replace   = true;
            bool doBackup  = replaceChoice == System.Windows.MessageBoxResult.Yes;
            string? backupDir = doBackup
                ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(slnPath)!, "_vbmigrator_backup")
                : null;

            // Capture VB projects in solution BEFORE CLI deletes .vbproj from disk
            var vbProjects = new System.Collections.Generic.List<(string VbprojPath, string CsprojPath)>();
            if (dte?.Solution?.Projects != null)
            {
                foreach (EnvDTE.Project proj in dte.Solution.Projects)
                {
                    var fn = proj?.FileName ?? "";
                    if (fn.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase))
                    {
                        var csproj = System.IO.Path.ChangeExtension(fn, ".csproj");
                        vbProjects.Add((fn, csproj));
                        // Close any open documents for this project to avoid VS file locks
                        try { proj!.ProjectItems?.Cast<EnvDTE.ProjectItem>()
                                  .Where(i => i.IsOpen[EnvDTE.Constants.vsViewKindAny])
                                  .ToList().ForEach(i => i.Document?.Close()); }
                        catch { /* best-effort */ }
                    }
                }
            }

            if (dte?.StatusBar != null)
                dte.StatusBar.Text = "VBMigrator: converting solution…";

            var progressLog = new System.Text.StringBuilder();
            var opts   = (Options.VBMigratorOptions)_package.GetDialogPage(typeof(Options.VBMigratorOptions));
            var runner = new CliRunner(opts.EffectiveApiKey);
            var exitCode = await runner.ConvertSolutionAsync(
                slnPath, outputDir: null,
                onProgress: line =>
                {
                    progressLog.AppendLine(line);
                    System.Diagnostics.Debug.WriteLine($"[VBMigrator] {line}");
                },
                replace: replace, backupDir: backupDir);

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

            // Swap .vbproj → .csproj in the VS solution
            if (replace && vbProjects.Count > 0 && dte?.Solution != null)
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                foreach (var (vbprojPath, csprojPath) in vbProjects)
                {
                    try
                    {
                        // Find and remove the VB project (file may already be deleted by CLI)
                        foreach (EnvDTE.Project proj in dte.Solution.Projects.Cast<EnvDTE.Project>().ToList())
                        {
                            if (string.Equals(proj.FileName, vbprojPath, StringComparison.OrdinalIgnoreCase))
                            {
                                dte.Solution.Remove(proj);
                                break;
                            }
                        }
                        // Add the new .csproj
                        if (System.IO.File.Exists(csprojPath))
                            dte.Solution.AddFromFile(csprojPath);
                    }
                    catch (Exception swapEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[VBMigrator] swap failed: {swapEx.Message}");
                    }
                }
                dte.Solution.SaveAs(slnPath);

                if (dte.StatusBar != null)
                    dte.StatusBar.Text = "VBMigrator: solution updated.";
            }

            var window = await _package.ShowToolWindowAsync(
                typeof(ToolWindows.ReviewQueueWindow), 0, true, _package.DisposalToken)
                as ToolWindows.ReviewQueueWindow;
            if (window?.Control != null)
                await window.Control.LoadQueueAsync();
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
