using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using VBMigrator.VSIX.Services;

namespace VBMigrator.VSIX.ToolWindows;

public partial class ReviewQueueWindowControl : UserControl
{
    private ReviewQueueItem? _current;

    public ReviewQueueWindowControl() => InitializeComponent();

    public async System.Threading.Tasks.Task LoadQueueAsync()
    {
        try
        {
            var runner = new CliRunner();
            var items  = await runner.GetQueueAsync();
            QueueList.ItemsSource = items;
            Header.Text = $"Review Queue ({items.Count} pendientes)";
        }
        catch (Exception ex)
        {
            Header.Text = $"Review Queue — error: {ex.Message}";
        }
    }

    private void QueueList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _current     = QueueList.SelectedItem as ReviewQueueItem;
        VbPanel.Text = _current?.VbSource ?? "";
        CsPanel.Text = _current?.CsSource ?? "";
    }

    private async void Accept_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        try
        {
            await new CliRunner().AcceptQueueItemAsync(_current.Id);
            await LoadQueueAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"VBMigrator error: {ex.Message}", "VBMigrator");
        }
    }

    private async void EditAndLearn_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var vb  = VbPanel.Text.Trim();
        var cs  = CsPanel.Text.Trim();
        var tag = _current.Tag ?? "manual";
        if (string.IsNullOrEmpty(vb) || string.IsNullOrEmpty(cs)) return;

        try
        {
            var saveArgs = $"kb save --vb \"{vb.Replace("\"", "\\\"")}\" --cs \"{cs.Replace("\"", "\\\"")}\" --tag \"{tag}\"";
            var psi = new ProcessStartInfo(CliLocator.FindExecutable(), saveArgs)
            {
                CreateNoWindow  = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit();

            await new CliRunner().AcceptQueueItemAsync(_current.Id);
            MessageBox.Show("Correction saved to knowledge base.", "VBMigrator");
            await LoadQueueAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"VBMigrator error: {ex.Message}", "VBMigrator");
        }
    }

    private async void Manual_Click(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        try
        {
            await new CliRunner().DismissQueueItemAsync(_current.Id);
            await LoadQueueAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"VBMigrator error: {ex.Message}", "VBMigrator");
        }
    }
}
