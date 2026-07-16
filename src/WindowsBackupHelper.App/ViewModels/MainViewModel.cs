using CommunityToolkit.Mvvm.ComponentModel;

namespace WindowsBackupHelper.App.ViewModels;

public sealed partial class MainViewModel(
    JobsViewModel jobsViewModel,
    CredentialTargetsViewModel credentialTargetsViewModel,
    SettingsViewModel settingsViewModel,
    RunHistoryViewModel runHistoryViewModel,
    StandaloneVerificationViewModel standaloneVerificationViewModel) : ObservableObject
{
    public JobsViewModel Jobs { get; } = jobsViewModel;
    public CredentialTargetsViewModel Credentials { get; } = credentialTargetsViewModel;
    public SettingsViewModel Settings { get; } = settingsViewModel;
    public RunHistoryViewModel RunHistory { get; } = runHistoryViewModel;
    public StandaloneVerificationViewModel StandaloneVerification { get; } = standaloneVerificationViewModel;

    public async Task InitializeAsync()
    {
        // Credentials before Jobs: the Jobs tab's folder-pair editor needs the credential list
        // populated for its source/destination combo boxes.
        await Credentials.LoadAsync();
        await Settings.LoadAsync();
        await Jobs.LoadAsync();
        await RunHistory.LoadAsync();
    }
}
