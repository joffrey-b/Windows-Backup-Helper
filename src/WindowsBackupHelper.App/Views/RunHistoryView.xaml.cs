using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WindowsBackupHelper.App.ViewModels;

namespace WindowsBackupHelper.App.Views;

public partial class RunHistoryView : UserControl
{
    public RunHistoryView() => InitializeComponent();

    private void ResultsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (((DataGrid)sender).SelectedItem is not FolderPairRunResultRow { Result.RobocopyLogFilePath: { } logFilePath })
        {
            return;
        }

        if (!File.Exists(logFilePath))
        {
            MessageBox.Show($"Log file not found:\n{logFilePath}", "Log file missing", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Process.Start(new ProcessStartInfo(logFilePath) { UseShellExecute = true });
    }
}
