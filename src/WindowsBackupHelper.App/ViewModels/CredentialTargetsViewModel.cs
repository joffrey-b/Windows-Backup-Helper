using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsBackupHelper.App.Views;
using WindowsBackupHelper.Core.Credentials;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;

namespace WindowsBackupHelper.App.ViewModels;

public sealed partial class CredentialTargetsViewModel(
    CredentialTargetRepository repository, FolderPairRepository folderPairRepository, ICredentialStore credentialStore) : ObservableObject
{
    public ObservableCollection<CredentialTarget> Targets { get; } = [];

    [ObservableProperty]
    private CredentialTarget? _selectedTarget;

    public async Task LoadAsync()
    {
        Targets.Clear();
        foreach (var target in await repository.GetAllAsync())
        {
            Targets.Add(target);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var editorViewModel = new CredentialTargetEditorViewModel(repository, credentialStore, target: null);
        var window = new CredentialTargetEditorWindow { DataContext = editorViewModel, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (SelectedTarget is null)
        {
            return;
        }

        var editorViewModel = new CredentialTargetEditorViewModel(repository, credentialStore, SelectedTarget);
        var window = new CredentialTargetEditorWindow { DataContext = editorViewModel, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true)
        {
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedTarget is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"Delete credential '{SelectedTarget.Label}'? Any folder pair referencing it will need a new credential assigned.",
                "Delete credential", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        // Delete from Credential Manager FIRST, before touching the database: it's the one step
        // that can fail for reasons other than "already gone" (access denied, vault corruption),
        // and running it first means a failure here leaves the database untouched instead of
        // silently leaving folder pairs with their credential reference already cleared while
        // the credential (and its row) still exist.
        try
        {
            credentialStore.Delete(SelectedTarget.CredentialManagerTargetName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't delete the credential: {ex.Message}", "Delete failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // FolderPair.Source/DestinationCredentialTargetId has no ON DELETE CASCADE/SET NULL, so
        // deleting the credential row below while a folder pair still references it would
        // otherwise throw a foreign key violation instead of the graceful "cleared" behavior
        // this confirmation dialog promises.
        await folderPairRepository.ClearCredentialReferencesAsync(SelectedTarget.Id);
        await repository.DeleteAsync(SelectedTarget.Id);
        Targets.Remove(SelectedTarget);
        SelectedTarget = null;
    }
}
