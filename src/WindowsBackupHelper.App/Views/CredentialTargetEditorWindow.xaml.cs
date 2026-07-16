using System.Windows;
using WindowsBackupHelper.App.ViewModels;

namespace WindowsBackupHelper.App.Views;

public partial class CredentialTargetEditorWindow : Window
{
    public CredentialTargetEditorWindow() => InitializeComponent();

    private CredentialTargetEditorViewModel ViewModel => (CredentialTargetEditorViewModel)DataContext;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (await ViewModel.SaveAsync(PasswordBox.Password))
        {
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
