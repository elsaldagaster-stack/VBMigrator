using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VBMigrator.VSIX.Services;

namespace VBMigrator.VSIX.Commands;

public sealed class ConvertFolderCommand
{
    public const int CommandId = 0x0104;
    public static readonly Guid CommandSet = ConvertFileCommand.CommandSet;

    private readonly AsyncPackage _package;

    private ConvertFolderCommand(AsyncPackage package, OleMenuCommandService svc)
    {
        _package = package;
        svc.AddCommand(new OleMenuCommand(Execute, new CommandID(CommandSet, CommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var svc = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (svc != null) new ConvertFolderCommand(package, svc);
    }

    private async void Execute(object sender, EventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string folderPath;
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description      = "Select Web Site root folder to convert VB.NET → C#";
                dlg.ShowNewFolderButton = false;
                if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
                folderPath = dlg.SelectedPath;
            }

            var replaceChoice = System.Windows.MessageBox.Show(
                "Replace source files after conversion?\n\n" +
                "  • .vb files → deleted (replaced by .cs)\n\n" +
                "Yes    = replace and create backup\n" +
                "No     = convert only, keep .vb files\n" +
                "Cancel = abort",
                "VBMigrator — Replace files?",
                System.Windows.MessageBoxButton.YesNoCancel,
                System.Windows.MessageBoxImage.Question);

            if (replaceChoice == System.Windows.MessageBoxResult.Cancel) return;

            bool replace  = replaceChoice == System.Windows.MessageBoxResult.Yes;
            string? backupDir = replace
                ? System.IO.Path.Combine(folderPath, "_vbmigrator_backup")
                : null;

            var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            if (dte?.StatusBar != null)
                dte.StatusBar.Text = "VBMigrator: converting folder…";

            var progressLog = new System.Text.StringBuilder();
            var opts   = (Options.VBMigratorOptions)_package.GetDialogPage(typeof(Options.VBMigratorOptions));
            var runner = new CliRunner(opts.EffectiveApiKey);
            var exitCode = await runner.ConvertFolderAsync(
                folderPath, outputDir: null,
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
