using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowsBackupHelper.Core.Credentials;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;

namespace WindowsBackupHelper.App.ViewModels;

public sealed partial class CredentialTargetEditorViewModel : ObservableObject
{
    private readonly CredentialTargetRepository _repository;
    private readonly ICredentialStore _credentialStore;
    private readonly CredentialTarget? _existing;

    [ObservableProperty]
    private string _label = "";

    [ObservableProperty]
    private string _hostOrUncRoot = "";

    [ObservableProperty]
    private string _userName = "";

    public bool IsNew => _existing is null;

    public CredentialTargetEditorViewModel(CredentialTargetRepository repository, ICredentialStore credentialStore, CredentialTarget? target)
    {
        _repository = repository;
        _credentialStore = credentialStore;
        _existing = target;

        if (target is not null)
        {
            Label = target.Label;
            HostOrUncRoot = target.HostOrUncRoot;
            UserName = credentialStore.TryRead(target.CredentialManagerTargetName)?.UserName ?? "";
        }
    }

    /// <summary>The password is read straight from the PasswordBox in code-behind (never bound), so it's called out here explicitly.</summary>
    public async Task<bool> SaveAsync(string password)
    {
        if (string.IsNullOrWhiteSpace(Label) || string.IsNullOrWhiteSpace(HostOrUncRoot))
        {
            MessageBox.Show("Label and host/UNC root are both required.", "Missing fields", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (string.IsNullOrEmpty(password) && IsNew)
        {
            MessageBox.Show("A password is required for a new credential.", "Missing password", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var target = _existing ?? new CredentialTarget
        {
            Id = Guid.NewGuid().ToString(),
            Label = Label,
            HostOrUncRoot = HostOrUncRoot,
            CredentialManagerTargetName = $"WindowsBackupHelper:{Guid.NewGuid()}",
        };
        target.Label = Label;
        target.HostOrUncRoot = HostOrUncRoot;

        try
        {
            if (!string.IsNullOrEmpty(password))
            {
                _credentialStore.Save(target.CredentialManagerTargetName, UserName, password);
            }
            else if (_existing is not null)
            {
                // Blank password means "keep the existing password" -- but the username can
                // still have changed, and CredentialTarget itself has no username column (both
                // live only in Windows Credential Manager), so a username-only edit must still
                // re-save, just with the existing password instead of a new one.
                var existingCredential = _credentialStore.TryRead(target.CredentialManagerTargetName);
                if (existingCredential is not null && existingCredential.UserName != UserName)
                {
                    _credentialStore.Save(target.CredentialManagerTargetName, UserName, existingCredential.Password);
                }
            }
        }
        catch (Exception ex)
        {
            // CredWrite can fail (oversized blob, vault corruption, Credential Manager entry
            // limits); with no DispatcherUnhandledException handler anywhere in the app, letting
            // this escape Save_Click's async void would crash the whole application.
            MessageBox.Show($"Couldn't save the credential: {ex.Message}", "Credential save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        if (_existing is null)
        {
            await _repository.InsertAsync(target);
        }
        else
        {
            await _repository.UpdateAsync(target);
        }

        return true;
    }
}
