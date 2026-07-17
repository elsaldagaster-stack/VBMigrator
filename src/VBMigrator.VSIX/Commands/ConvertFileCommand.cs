using System;
using System.ComponentModel.Design;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using VBMigrator.VSIX.Services;

namespace VBMigrator.VSIX.Commands;

public sealed class ConvertFileCommand
{
    public const int CommandId = 0x0100;
    public static readonly Guid CommandSet = new("b1c2d3e4-f5a6-7890-bcde-f01234567891");

    private readonly AsyncPackage _package;

    private ConvertFileCommand(AsyncPackage package, OleMenuCommandService commandService)
    {
        _package = package;
        var id  = new CommandID(CommandSet, CommandId);
        var cmd = new OleMenuCommand(Execute, id);
        cmd.BeforeQueryStatus += OnBeforeQueryStatus;
        commandService.AddCommand(cmd);
    }

    private void OnBeforeQueryStatus(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (sender is not OleMenuCommand cmd) return;
        var dte      = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
        var filePath = dte?.SelectedItems?.Item(1)?.ProjectItem?.FileNames[1] ?? "";
        cmd.Visible  = filePath.EndsWith(".vb",   StringComparison.OrdinalIgnoreCase)
                    || filePath.EndsWith(".aspx",  StringComparison.OrdinalIgnoreCase);
    }

    public static async Task InitializeAsync(AsyncPackage package)
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
        var svc = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
        if (svc != null) new ConvertFileCommand(package, svc);
    }

    private async void Execute(object sender, EventArgs e)
    {
        try
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var selectedItem = dte?.SelectedItems?.Item(1)?.ProjectItem;
            var filePath = selectedItem?.FileNames[1];
            if (filePath == null) return;

            // .aspx → redirect to code-behind .aspx.vb
            if (filePath.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase))
            {
                var codeBehind = filePath + ".vb";
                if (!File.Exists(codeBehind)) return;
                filePath = codeBehind;
            }
            else if (!filePath.EndsWith(".vb", StringComparison.OrdinalIgnoreCase))
                return;

            var opts   = (Options.VBMigratorOptions)_package.GetDialogPage(typeof(Options.VBMigratorOptions));
            var runner = new CliRunner(opts.EffectiveApiKey);
            var result = await runner.ConvertFileAsync(filePath);
            if (result == null) return;

            if (result.Route == "HumanQueue")
            {
                await _package.ShowToolWindowAsync(
                    typeof(ToolWindows.ReviewQueueWindow), 0, true, _package.DisposalToken);
            }
            else
            {
                var csPath = Path.ChangeExtension(filePath, ".cs");
                await Task.Run(() => File.WriteAllText(csPath, result.CsSource));
            }
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
