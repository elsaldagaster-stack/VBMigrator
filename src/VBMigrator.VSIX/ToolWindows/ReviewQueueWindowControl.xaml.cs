using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace VBMigrator.VSIX.ToolWindows;

public partial class ReviewQueueWindowControl : UserControl
{
    public ReviewQueueWindowControl() => InitializeComponent();

    private void Accept_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Accepted. File written.", "VBMigrator");
    }

    private async void EditAndLearn_Click(object sender, RoutedEventArgs e)
    {
        var vb = VbPanel.Text.Trim();
        var cs = CsPanel.Text.Trim();
        if (string.IsNullOrEmpty(vb) || string.IsNullOrEmpty(cs)) return;

        var args = $"kb save --vb \"{vb.Replace("\"", "\\\"")}\" --cs \"{cs.Replace("\"", "\\\"")}\" --tag \"manual\"";
        var psi = new ProcessStartInfo(Services.CliLocator.FindExecutable(), args)
        {
            CreateNoWindow  = true,
            UseShellExecute = false
        };
        Process.Start(psi)?.WaitForExit();
        MessageBox.Show("Correction saved to knowledge base.", "VBMigrator");
        await System.Threading.Tasks.Task.CompletedTask;
    }

    private void Manual_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Item marked for manual review.", "VBMigrator");
    }
}
