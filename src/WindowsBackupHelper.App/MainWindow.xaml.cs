using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WindowsBackupHelper.App.ViewModels;

namespace WindowsBackupHelper.App;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closing += MainWindow_Closing;
    }

    /// <summary>
    /// Run History isn't reloaded automatically as runs happen elsewhere in the app, so
    /// refresh it every time the user navigates to that tab rather than only at startup.
    /// </summary>
    private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && ReferenceEquals(e.AddedItems[0], RunHistoryTab) && DataContext is MainViewModel viewModel)
        {
            _ = viewModel.RunHistory.LoadAsync();
        }
    }

    /// <summary>
    /// robocopy.exe has no OS Job Object tying its lifetime to this process, so closing the
    /// window while a job is running would otherwise just orphan it — it keeps copying in the
    /// background, invisible to the app. Confirm first, then cancel and wait for the run to
    /// actually finish tearing down before letting the window close for real.
    /// </summary>
    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || DataContext is not MainViewModel viewModel || !viewModel.Jobs.IsBusy)
        {
            return;
        }

        e.Cancel = true;

        var result = MessageBox.Show(
            "A backup job is currently running. Closing now will cancel it partway through — this can leave a partially-copied file at the destination.\n\nAre you sure you want to close?",
            "Job running", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        viewModel.Jobs.CancelRunningJob();

        while (viewModel.Jobs.IsBusy)
        {
            await Task.Delay(100);
        }

        _closeConfirmed = true;
        Close();
    }
}
