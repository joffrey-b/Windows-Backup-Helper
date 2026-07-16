using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WindowsBackupHelper.Core.Data;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Robocopy;

namespace WindowsBackupHelper.App.ViewModels;

public sealed partial class SettingsViewModel(
    AppSettingsRepository repository, RobocopyOptionSetRepository optionSetRepository, AppSettingsCache cache) : ObservableObject
{
    private AppSettings? _appSettings;

    [ObservableProperty]
    private string? _flacExecutablePath;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private int? _defaultChecksumWorkers = 4;

    [ObservableProperty]
    private int? _defaultFlacWorkers = 8;

    [ObservableProperty]
    private RobocopyOptionSet? _defaultOptionSet;

    [ObservableProperty]
    private string? _statusMessage;

    public async Task LoadAsync()
    {
        _appSettings = await repository.GetAsync() ?? throw new InvalidOperationException("AppSettings is missing — DatabaseSeeder should have created it.");
        FlacExecutablePath = _appSettings.FlacExecutablePath;
        NotificationsEnabled = _appSettings.NotificationsEnabled;
        DefaultChecksumWorkers = _appSettings.DefaultChecksumWorkers;
        DefaultFlacWorkers = _appSettings.DefaultFlacWorkers;
        var defaultOptionSet = await optionSetRepository.GetByIdAsync(_appSettings.DefaultRobocopyOptionSetId);

        // App-level defaults are the top of the inheritance chain — there's no level above to
        // "inherit" from, so a null here would only ever mean "off". Backfill to concrete
        // true/false BEFORE assigning to the bound property: RobocopyOptionSet has no
        // INotifyPropertyChanged of its own, so mutating it in place after DefaultOptionSet's
        // PropertyChanged has already fired would leave any already-bound checkbox showing
        // stale (indeterminate) values until the next reload.
        if (defaultOptionSet is not null)
        {
            RobocopyOptionsResolver.BackfillNullBooleans(defaultOptionSet, defaultOptionSet);
        }

        DefaultOptionSet = defaultOptionSet;
    }

    [RelayCommand]
    private void BrowseFlacExecutablePath()
    {
        var dialog = new OpenFileDialog { Filter = "flac.exe|flac.exe|All files (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
        {
            FlacExecutablePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_appSettings is null || DefaultOptionSet is null)
        {
            return;
        }

        // Hard requirement: every job's option resolution ultimately falls back to this row,
        // and Robocopy's own defaults (/R:1000000 /W:30) are effectively "hang forever".
        if (DefaultOptionSet.Retries is null || DefaultOptionSet.WaitSeconds is null)
        {
            MessageBox.Show(
                "Retries and Wait seconds are required for the app-level defaults.",
                "Missing values", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Same reasoning: these are the ultimate fallback for every job/pair's worker count --
        // unlike the per-pair/standalone checksum/FLAC worker fields, there's no lower level
        // left to inherit a blank from here, so it must be rejected rather than saved as null.
        if (DefaultChecksumWorkers is null || DefaultFlacWorkers is null)
        {
            MessageBox.Show(
                "Default checksum workers and default FLAC workers are both required.",
                "Missing values", MessageBoxButton.OK, MessageBoxImage.Warning);

            // Snap whichever field(s) were left blank back to the last saved value, rather than
            // leaving the box empty indefinitely (it wouldn't refill on its own — nothing
            // reloads Settings on a tab switch) while a valid value is still saved underneath.
            DefaultChecksumWorkers ??= _appSettings.DefaultChecksumWorkers;
            DefaultFlacWorkers ??= _appSettings.DefaultFlacWorkers;
            return;
        }

        await optionSetRepository.UpdateAsync(DefaultOptionSet);

        _appSettings.FlacExecutablePath = FlacExecutablePath;
        _appSettings.NotificationsEnabled = NotificationsEnabled;
        _appSettings.DefaultChecksumWorkers = DefaultChecksumWorkers.Value;
        _appSettings.DefaultFlacWorkers = DefaultFlacWorkers.Value;
        await repository.UpdateAsync(_appSettings);
        cache.Update(_appSettings);

        StatusMessage = $"Saved at {DateTime.Now:T}.";
    }
}
