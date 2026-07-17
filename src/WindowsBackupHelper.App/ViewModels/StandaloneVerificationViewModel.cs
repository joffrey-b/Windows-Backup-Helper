using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using WindowsBackupHelper.Core.Credentials;
using WindowsBackupHelper.Core.Execution;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Smb;

namespace WindowsBackupHelper.App.ViewModels;

/// <summary>
/// The "Verify Backup" tab: an ad-hoc checksum/FLAC check against an arbitrary folder, with no
/// Job/FolderPair/RunHistory involved and no Robocopy run — for periodically re-checking a
/// backup that's already on disk, as opposed to the Folder Pair editor's Verification tab, which
/// runs immediately after a copy as part of a job. See HelpView for the full rationale.
/// </summary>
public sealed partial class StandaloneVerificationViewModel(
    VerificationRunner verificationRunner,
    CredentialTargetsViewModel credentialTargetsViewModel,
    ICredentialStore credentialStore,
    ISmbConnectionManager smbConnectionManager) : ObservableObject
{
    public ObservableCollection<CredentialTarget> CredentialTargets => credentialTargetsViewModel.Targets;

    [ObservableProperty]
    private string? _folderPath;

    [ObservableProperty]
    private string? _credentialTargetId;

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
    [NotifyCanExecuteChangedFor(nameof(RunVerificationCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelVerificationCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _runProgressText;

    [ObservableProperty]
    private string? _resultMessage;

    private CancellationTokenSource? _runCancellationTokenSource;

    private bool CanRun() => !IsBusy;

    private bool CanCancel() => IsBusy;

    [RelayCommand]
    private void BrowseFolder()
    {
        var dialog = new OpenFolderDialog();
        if (dialog.ShowDialog() == true)
        {
            FolderPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void ClearCredential() => CredentialTargetId = null;

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

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void CancelVerification() => _runCancellationTokenSource?.Cancel();

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunVerificationAsync()
    {
        if (FolderPath is not { Length: > 0 } folderPath)
        {
            ResultMessage = "Choose a folder first.";
            return;
        }

        if (ChecksumMode == ChecksumMode.None && !RunFlacAudit)
        {
            ResultMessage = "Choose a checksum mode and/or enable the FLAC audit first.";
            return;
        }

        if (ChecksumMode != ChecksumMode.None && string.IsNullOrWhiteSpace(ChecksumManifestPath))
        {
            ResultMessage = $"'{ChecksumMode}' needs a manifest path — set one (or Browse to create one) first.";
            return;
        }

        var settings = new VerificationSettings
        {
            Id = Guid.NewGuid().ToString(), // never persisted -- just a carrier for this one run
            ChecksumMode = ChecksumMode,
            ChecksumManifestPath = ChecksumManifestPath,
            ChecksumWorkers = ChecksumWorkers,
            ChecksumReportOutputPath = ChecksumReportOutputPath,
            RunFlacAudit = RunFlacAudit,
            FlacReportOutputPath = FlacReportOutputPath,
            FlacErrorsOnly = FlacErrorsOnly,
            FlacWorkers = FlacWorkers,
        };

        IsBusy = true;
        RunProgressText = null;
        ResultMessage = null;
        _runCancellationTokenSource = new CancellationTokenSource();
        var progress = new Progress<VerificationProgress>(vp => RunProgressText = vp.FilesCompleted > 0
            ? $"{vp.PhaseMessage} {vp.FilesCompleted} file(s) processed so far."
            : vp.PhaseMessage);

        try
        {
            // A UNC path needs an authenticated SMB session before Directory.Exists (or anything
            // else) can see into it -- connecting first, then checking existence, mirrors
            // JobExecutionService.RunFolderPairAsync's order for the same reason.
            using var connection = ConnectIfNeeded(folderPath, CredentialTargetId);

            if (!Directory.Exists(folderPath))
            {
                ResultMessage = "Choose a folder that exists first.";
                return;
            }

            var result = await verificationRunner.RunAsync(folderPath, settings, _runCancellationTokenSource.Token, progress);

            var parts = new List<string>();
            if (result.ChecksumOutcomeSummary is { } checksumSummary)
            {
                parts.Add($"Checksum: {checksumSummary}");
            }

            if (result.FlacOutcomeSummary is { } flacSummary)
            {
                parts.Add($"FLAC: {flacSummary}");
            }

            ResultMessage = parts.Count > 0 ? string.Join("  ", parts) : "Nothing to do.";
        }
        catch (OperationCanceledException)
        {
            ResultMessage = "Verification cancelled.";
        }
        catch (Exception ex)
        {
            ResultMessage = $"Verification failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RunProgressText = null;
            _runCancellationTokenSource.Dispose();
            _runCancellationTokenSource = null;
        }
    }

    /// <summary>Same reasoning as JobExecutionService.ConnectIfNeededAsync, just synchronous and
    /// reading from the already-loaded CredentialTargets collection instead of a repository
    /// round-trip -- this tab has no job/pair to inherit a connection from, so a folder path
    /// under a network share needs its own SMB session established before anything can see
    /// into it.</summary>
    private IDisposable? ConnectIfNeeded(string path, string? credentialTargetId)
    {
        if (credentialTargetId is null || !path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        var credentialTarget = CredentialTargets.FirstOrDefault(t => t.Id == credentialTargetId);
        if (credentialTarget is null)
        {
            return null;
        }

        var storedCredential = credentialStore.TryRead(credentialTarget.CredentialManagerTargetName);
        if (storedCredential is null)
        {
            throw new InvalidOperationException(
                $"No credential found in Windows Credential Manager for '{credentialTarget.Label}' ({credentialTarget.CredentialManagerTargetName}).");
        }

        return smbConnectionManager.Connect(path, storedCredential.UserName, storedCredential.Password);
    }
}
