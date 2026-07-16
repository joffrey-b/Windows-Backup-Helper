using System.Windows;
using System.Windows.Controls;
using WindowsBackupHelper.App.ViewModels;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.App.Views;

public partial class JobsView : UserControl
{
    public JobsView() => InitializeComponent();

    private void FolderPairEnabledCheckBox_Toggled(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FolderPair pair && DataContext is JobsViewModel viewModel)
        {
            _ = viewModel.SaveFolderPairAsync(pair);
        }
    }
}
