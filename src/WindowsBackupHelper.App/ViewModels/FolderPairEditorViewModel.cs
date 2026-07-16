using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WindowsBackupHelper.Core.Checksums;
using WindowsBackupHelper.Core.Data;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Robocopy;

namespace WindowsBackupHelper.App.ViewModels;

public sealed partial class FolderPairEditorViewModel : ObservableObject
{
    private readonly FolderPairRepository _folderPairRepository;
    private readonly RobocopyOptionSetRepository _optionSetRepository;
    private readonly ExclusionRuleRepository _exclusionRuleRepository;
    private readonly VerificationSettingsRepository _verificationSettingsRepository;
    private readonly AppSettingsCache _appSettingsCache;
    private readonly RobocopyOptionSet? _jobOptionOverrides;
    private readonly bool _isNew;

    public FolderPair Pair { get; }

    // FolderPair is a plain Core POCO (no INotifyPropertyChanged, by design — Core stays
    // UI-framework-agnostic). Mirroring the two path fields here means the Browse commands
    // can update them and have the bound TextBoxes actually refresh; SaveAsync copies the
    // final values back into Pair before persisting.
    [ObservableProperty]
    private string _sourcePath;

    [ObservableProperty]
    private string _destinationPath;

    // Same reasoning as SourcePath/DestinationPath above: Pair doesn't notify, so the "Clear"
    // commands below couldn't make the combo boxes reflect the change without this mirror.
    [ObservableProperty]
    private string? _sourceCredentialTargetId;

    [ObservableProperty]
    private string? _destinationCredentialTargetId;

    public ObservableCollection<CredentialTarget> CredentialTargets { get; }

    /// <summary>A "no credential" sentinel for the combo boxes, since ComboBox needs a bindable item, not null.</summary>
    public static CredentialTarget NoCredential { get; } = new()
    {
        Id = "", Label = "(none — no credential needed)", HostOrUncRoot = "", CredentialManagerTargetName = "",
    };

    public ObservableCollection<ExclusionRule> ExclusionRules { get; } = [];

    [ObservableProperty]
    private RobocopyOptionSet? _pairOptionOverrides;

    [ObservableProperty]
    private ChecksumMode _checksumMode = ChecksumMode.None;

    [ObservableProperty]
    private string? _checksumManifestPath;

    [ObservableProperty]
    private int? _checksumWorkers;

    [ObservableProperty]
    private string? _checksumReportOutputPath;

    [ObservableProperty]
    private bool _runFlacAudit;

    [ObservableProperty]
    private string? _flacReportOutputPath;

    [ObservableProperty]
    private bool _flacErrorsOnly;

    [ObservableProperty]
    private int? _flacWorkers;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    [NotifyCanExecuteChangedFor(nameof(TogglePairOptionOverridesCommand))]
    private bool _isBusy;

    private string? _verificationSettingsId;
    private bool _isLoadingPairOptionOverrides;

    public bool? DialogResult { get; private set; }

    public FolderPairEditorViewModel(
        FolderPairRepository folderPairRepository,
        RobocopyOptionSetRepository optionSetRepository,
        ExclusionRuleRepository exclusionRuleRepository,
        VerificationSettingsRepository verificationSettingsRepository,
        AppSettingsCache appSettingsCache,
        RobocopyOptionSet? jobOptionOverrides,
        FolderPair pair,
        bool isNew,
        ObservableCollection<CredentialTarget> credentialTargets)
    {
        _folderPairRepository = folderPairRepository;
        _optionSetRepository = optionSetRepository;
        _exclusionRuleRepository = exclusionRuleRepository;
        _verificationSettingsRepository = verificationSettingsRepository;
        _appSettingsCache = appSettingsCache;
        _jobOptionOverrides = jobOptionOverrides;
        Pair = pair;
        _sourcePath = pair.SourcePath;
        _destinationPath = pair.DestinationPath;
        _sourceCredentialTargetId = pair.SourceCredentialTargetId;
        _destinationCredentialTargetId = pair.DestinationCredentialTargetId;
        _isNew = isNew;
        CredentialTargets = credentialTargets;
        _verificationSettingsId = pair.VerificationSettingsId;

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (Pair.PairRobocopyOptionSetId is { } optionSetId)
        {
            // Distinguishes "still fetching the existing override" from "user already toggled
            // it off in memory" for TogglePairOptionOverridesAsync's race guard below — both
            // would otherwise look identical (PairOptionOverrides null, but the pair's FK still
            // set, since only Save clears the FK).
            // try/finally so a failure partway through (e.g. the app-level default
            // RobocopyOptionSet row is missing) can't leave this stuck true forever, permanently
            // no-opping TogglePairOptionOverridesAsync with no error shown.
            _isLoadingPairOptionOverrides = true;
            try
            {
                var pairOverrides = await _optionSetRepository.GetByIdAsync(optionSetId);

                // Rows created before checkbox materialization existed (or edited directly in
                // the DB) can still have null boolean fields, which would render as an ambiguous
                // indeterminate checkbox even though IsThreeState is off. Backfill BEFORE
                // assigning to the bound property.
                if (pairOverrides is not null)
                {
                    var appDefaults = await _optionSetRepository.GetRequiredDefaultAsync(_appSettingsCache);
                    RobocopyOptionsResolver.BackfillNullBooleans(pairOverrides, appDefaults, _jobOptionOverrides);
                }

                PairOptionOverrides = pairOverrides;
            }
            finally
            {
                _isLoadingPairOptionOverrides = false;
            }
        }

        ExclusionRules.Clear();
        if (!_isNew)
        {
            foreach (var rule in await _exclusionRuleRepository.GetByFolderPairIdAsync(Pair.Id))
            {
                ExclusionRules.Add(rule);
            }
        }

        if (_verificationSettingsId is { } verificationId)
        {
            var settings = await _verificationSettingsRepository.GetByIdAsync(verificationId);
            if (settings is not null)
            {
                ChecksumMode = settings.ChecksumMode;
                ChecksumManifestPath = settings.ChecksumManifestPath;
                ChecksumWorkers = settings.ChecksumWorkers;
                ChecksumReportOutputPath = settings.ChecksumReportOutputPath;
                RunFlacAudit = settings.RunFlacAudit;
                FlacReportOutputPath = settings.FlacReportOutputPath;
                FlacErrorsOnly = settings.FlacErrorsOnly;
                FlacWorkers = settings.FlacWorkers;
            }
        }
    }

    private bool CanSave() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task TogglePairOptionOverridesAsync()
    {
        if (PairOptionOverrides is not null)
        {
            PairOptionOverrides = null;
            return;
        }

        // The constructor's fire-and-forget LoadAsync is still resolving the pair's existing
        // override — ignore this click rather than race it and clobber whichever assignment
        // finishes last. Can't simply check Pair.PairRobocopyOptionSetId here: once the user
        // has toggled overrides off in memory (the branch above), the FK is still set until
        // Save runs, which would otherwise make "still loading" and "already disabled, ready
        // to re-enable" indistinguishable.
        if (_isLoadingPairOptionOverrides)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var appDefaults = await _optionSetRepository.GetRequiredDefaultAsync(_appSettingsCache);
            PairOptionOverrides = RobocopyOptionsResolver.CreateMaterializedOverride(Guid.NewGuid().ToString(), appDefaults, _jobOptionOverrides);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Couldn't enable pair-level overrides: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void BrowseSourcePath() => BrowseForFolder(path => SourcePath = path);

    [RelayCommand]
    private void BrowseDestinationPath() => BrowseForFolder(path => DestinationPath = path);

    [RelayCommand]
    private void ClearSourceCredential() => SourceCredentialTargetId = null;

    [RelayCommand]
    private void ClearDestinationCredential() => DestinationCredentialTargetId = null;

    [RelayCommand]
    private void BrowseChecksumManifestPath()
    {
        var dialog = new SaveFileDialog { Filter = "SHA256 manifest (*.sha256)|*.sha256|All files (*.*)|*.*", OverwritePrompt = false };
        if (dialog.ShowDialog() == true)
        {
            ChecksumManifestPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseChecksumReportOutputPath()
    {
        var dialog = new SaveFileDialog { Filter = "Markdown report (*.md)|*.md|All files (*.*)|*.*", OverwritePrompt = false };
        if (dialog.ShowDialog() == true)
        {
            ChecksumReportOutputPath = dialog.FileName;
        }
    }

    [RelayCommand]
    private void BrowseFlacReportOutputPath()
    {
        var dialog = new SaveFileDialog { Filter = "Markdown report (*.md)|*.md|All files (*.*)|*.*", OverwritePrompt = false };
        if (dialog.ShowDialog() == true)
        {
            FlacReportOutputPath = dialog.FileName;
        }
    }

    private static void BrowseForFolder(Action<string> onSelected)
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            onSelected(dialog.FolderName);
        }
    }

    [RelayCommand]
    private void AddExclusionRule() => ExclusionRules.Add(new ExclusionRule
    {
        Scope = ExclusionScope.FolderPair,
        FolderPairId = Pair.Id,
        PatternType = ExclusionPatternType.Wildcard,
        Pattern = "*.tmp",
        TargetType = ExclusionTargetType.File,
    });

    [RelayCommand]
    private void RemoveExclusionRule(ExclusionRule? rule)
    {
        if (rule is not null)
        {
            ExclusionRules.Remove(rule);
        }
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync(Window window)
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(DestinationPath))
        {
            MessageBox.Show("Source and destination paths are both required.", "Missing paths", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // VerificationRunner throws for exactly this case at job-run time -- catching it here
        // instead means the job's already-computed copy stats never get discarded and replaced
        // with a bare error row just because a manifest path was left blank.
        if (ChecksumMode != ChecksumMode.None && string.IsNullOrWhiteSpace(ChecksumManifestPath))
        {
            MessageBox.Show($"'{ChecksumMode}' needs a manifest path — set one (or Browse to create one) first.", "Missing manifest path", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Pair.SourcePath = SourcePath;
        Pair.DestinationPath = DestinationPath;
        Pair.SourceCredentialTargetId = SourceCredentialTargetId;
        Pair.DestinationCredentialTargetId = DestinationCredentialTargetId;

        string? removedOptionSetId = null;
        if (PairOptionOverrides is not null)
        {
            if (Pair.PairRobocopyOptionSetId is null)
            {
                await _optionSetRepository.InsertAsync(PairOptionOverrides);
                Pair.PairRobocopyOptionSetId = PairOptionOverrides.Id;
            }
            else
            {
                await _optionSetRepository.UpdateAsync(PairOptionOverrides);
            }
        }
        else if (Pair.PairRobocopyOptionSetId is { } existingOptionSetId)
        {
            // The user toggled overrides off for a pair that previously had them — clear the FK
            // now, but defer actually deleting the now-orphaned row until after the FolderPair
            // itself is persisted below: SQLite's foreign key check runs at DELETE time, and
            // would reject removing a row the database's own copy of this pair still points to.
            removedOptionSetId = existingOptionSetId;
            Pair.PairRobocopyOptionSetId = null;
        }

        string? removedVerificationSettingsId = null;
        if (ChecksumMode != ChecksumMode.None || RunFlacAudit)
        {
            var settings = new VerificationSettings
            {
                Id = _verificationSettingsId ?? Guid.NewGuid().ToString(),
                ChecksumMode = ChecksumMode,
                ChecksumManifestPath = ChecksumManifestPath,
                ChecksumWorkers = ChecksumWorkers,
                ChecksumReportOutputPath = ChecksumReportOutputPath,
                RunFlacAudit = RunFlacAudit,
                FlacReportOutputPath = FlacReportOutputPath,
                FlacErrorsOnly = FlacErrorsOnly,
                FlacWorkers = FlacWorkers,
            };

            if (_verificationSettingsId is null)
            {
                await _verificationSettingsRepository.InsertAsync(settings);
                _verificationSettingsId = settings.Id;
                Pair.VerificationSettingsId = settings.Id;
            }
            else
            {
                await _verificationSettingsRepository.UpdateAsync(settings);
            }
        }
        else if (_verificationSettingsId is { } existingVerificationSettingsId)
        {
            // Same reasoning and same deferred-delete fix as removedOptionSetId above.
            removedVerificationSettingsId = existingVerificationSettingsId;
            _verificationSettingsId = null;
            Pair.VerificationSettingsId = null;
        }

        if (_isNew)
        {
            await _folderPairRepository.InsertAsync(Pair);
        }
        else
        {
            await _folderPairRepository.UpdateAsync(Pair);
        }

        // Only now that the FolderPair row's own FK columns have actually been cleared in the
        // database is it safe to delete the rows they used to reference.
        if (removedOptionSetId is not null)
        {
            await _optionSetRepository.DeleteAsync(removedOptionSetId);
        }

        if (removedVerificationSettingsId is not null)
        {
            await _verificationSettingsRepository.DeleteAsync(removedVerificationSettingsId);
        }

        foreach (var rule in ExclusionRules)
        {
            if (rule.Id == 0)
            {
                rule.Id = await _exclusionRuleRepository.InsertAsync(rule);
            }
            else
            {
                await _exclusionRuleRepository.UpdateAsync(rule);
            }
        }

        DialogResult = true;
        window.DialogResult = true;
        window.Close();
    }

    [RelayCommand]
    private void Cancel(Window window)
    {
        DialogResult = false;
        window.DialogResult = false;
        window.Close();
    }
}
