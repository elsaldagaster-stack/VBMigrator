using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VBMigrator.VSIX.Services;

namespace VBMigrator.VSIX.Commands;

public sealed class ConvertSolutionCommand
{
    public const int CommandId = 0x0102;
    public static readonly Guid CommandSet = ConvertFileCommand.CommandSet;

    private readonly AsyncPackage _package;

    private ConvertSolutionCommand(AsyncPackage package, OleMenuCommandService svc)
    {
        _package = package;
        svc.AddCommand(new OleMenuCommand(Execute, new CommandID(CommandSet, CommandId)));
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var svc = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (svc != null) new ConvertSolutionCommand(package, svc);
    }

    private async void Execute(object sender, EventArgs e)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        var slnPath = dte?.Solution?.FullName;
        if (slnPath == null) return;

        var runner = new CliRunner();
        var exitCode = await runner.ConvertSolutionAsync(
            slnPath, outputDir: null,
            onProgress: line => System.Diagnostics.Debug.WriteLine($"[VBMigrator] {line}"));

        if (exitCode == 0)
            await _package.ShowToolWindowAsync(
                typeof(ToolWindows.ReviewQueueWindow), 0, true, _package.DisposalToken);
    }
}
