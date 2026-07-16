using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace VBMigrator.VSIX;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[Guid(PackageGuidString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(ToolWindows.ReviewQueueWindow))]
public sealed class VBMigratorPackage : AsyncPackage
{
    public const string PackageGuidString = "a1b2c3d4-e5f6-7890-abcd-ef1234567890";

    protected override async Task InitializeAsync(
        CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        await Commands.ConvertFileCommand.InitializeAsync(this);
        await Commands.ConvertSolutionCommand.InitializeAsync(this);
    }
}
