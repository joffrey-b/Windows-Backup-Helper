using System.Windows;
using WindowsBackupHelper.App.ViewModels;

namespace WindowsBackupHelper.App.Views;

public partial class ScheduleEditorWindow : Window
{
    public ScheduleEditorWindow() => InitializeComponent();

    private ScheduleEditorViewModel ViewModel => (ScheduleEditorViewModel)DataContext;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.SaveAsync(AccountPasswordBox.Password))
        {
            DialogResult = true;
            Close();
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RemoveScheduleAsync();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
