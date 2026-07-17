using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.Sqlite;
using WindowsBackupHelper.App.Services;
using WindowsBackupHelper.App.Views;
using WindowsBackupHelper.Core.Data;
using WindowsBackupHelper.Core.Execution;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Robocopy;
using WindowsBackupHelper.Core.Scheduling;

namespace WindowsBackupHelper.App.ViewModels;

public sealed partial class JobsViewModel(
    JobRepository jobRepository,
    FolderPairRepository folderPairRepository,
    RobocopyOptionSetRepository optionSetRepository,
    ExclusionRuleRepository exclusionRuleRepository,
    VerificationSettingsRepository verificationSettingsRepository,
    CredentialTargetsViewModel credentialTargetsViewModel,
    ScheduleMetadataRepository scheduleMetadataRepository,
    ITaskSchedulerService taskSchedulerService,
    AppSettingsCache appSettingsCache,
    JobExecutionService jobExecutionService) : ObservableObject
{
    public ObservableCollection<Job> Jobs { get; } = [];

    public ObservableCollection<FolderPair> FolderPairs { get; } = [];

    /// <summary>
    /// Drift-detection banners: ScheduleMetadata's cached fields compared against the live OS
    /// task on load, surfaced here if the user hand-edited a task outside the app.
    /// </summary>
    public ObservableCollection<string> ScheduleDriftWarnings { get; } = [];

    [ObservableProperty]
    private Job? _selectedJob;

    [ObservableProperty]
    private FolderPair? _selectedFolderPair;

    [ObservableProperty]
    private RobocopyOptionSet? _jobOptionOverrides;

    [ObservableProperty]
    private string? _statusMessage;

    /// <summary>Live status line for the run currently in progress, or null when nothing is
    /// running: "N file(s) processed so far" during the Robocopy copy itself, then a phase
    /// message ("Verifying checksums. This may take a while.", etc.) once post-copy
    /// verification/FLAC auditing starts for the current pair.</summary>
    [ObservableProperty]
    private string? _runProgressText;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveJobCommand))]
    [NotifyCanExecuteChangedFor(nameof(ToggleJobOptionOverridesCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelRunningJobCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunJobCommand))]
    [NotifyCanExecuteChangedFor(nameof(DryRunJobCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunSelectedPairCommand))]
    private bool _isBusy;

    private bool _isLoadingJobOptionOverrides;
    private CancellationTokenSource? _runCancellationTokenSource;

    partial void OnSelectedJobChanged(Job? value) => _ = LoadFolderPairsAsync(value);

    private bool CanCancelRunningJob() => IsBusy;

    /// <summary>
    /// Requests cancellation of the currently-running job, if any — bindable to a "Cancel"
    /// button (only enabled while a job is running), and also called by MainWindow's Closing
    /// handler to confirm-then-cancel rather than leaving robocopy orphaned in the background
    /// when the app is closed mid-run (there's no OS Job Object tying its lifetime to this
    /// process, so an abrupt close would otherwise let it keep running unsupervised).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCancelRunningJob))]
    public void CancelRunningJob() => _runCancellationTokenSource?.Cancel();

    public async Task LoadAsync()
    {
        Jobs.Clear();
        var jobs = await jobRepository.GetAllAsync();
        foreach (var job in jobs)
        {
            Jobs.Add(job);
        }

        // Credentials are loaded by CredentialTargetsViewModel itself (MainViewModel.InitializeAsync
        // calls Credentials.LoadAsync() before Jobs.LoadAsync()) — the folder pair editor binds
        // directly to that same collection instance (see EditFolderPairAsync below) so it always
        // reflects the latest adds/edits/deletes from the Credentials tab without needing a
        // separate reload here.
        await DetectScheduleDriftAsync(jobs);
    }

    private async Task DetectScheduleDriftAsync(IReadOnlyList<Job> jobs)
    {
        ScheduleDriftWarnings.Clear();
        var jobNamesById = jobs.ToDictionary(j => j.Id, j => j.Name);

        foreach (var metadata in await scheduleMetadataRepository.GetAllAsync())
        {
            var liveInfo = taskSchedulerService.GetLiveTaskInfo(metadata.TaskSchedulerTaskName);
            var jobName = jobNamesById.GetValueOrDefault(metadata.JobId, metadata.JobId);

            if (liveInfo is null)
            {
                ScheduleDriftWarnings.Add($"'{jobName}': its scheduled task was removed outside the app.");
            }
            else if (liveInfo.IsEnabled != metadata.IsEnabled || liveInfo.TriggerDescription != metadata.TriggerDescription)
            {
                ScheduleDriftWarnings.Add($"'{jobName}': its scheduled task was edited directly in Task Scheduler and no longer matches what's saved here.");
            }
        }
    }

    [RelayCommand]
    private async Task ScheduleSelectedJobAsync()
    {
        if (SelectedJob is null)
        {
            return;
        }

        var editorViewModel = new ScheduleEditorViewModel(taskSchedulerService, scheduleMetadataRepository, SelectedJob);
        var window = new ScheduleEditorWindow { DataContext = editorViewModel, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true)
        {
            await DetectScheduleDriftAsync(Jobs);
        }
    }

    private async Task LoadFolderPairsAsync(Job? job)
    {
        FolderPairs.Clear();
        JobOptionOverrides = null;
        _isLoadingJobOptionOverrides = false;
        if (job is null)
        {
            return;
        }

        foreach (var pair in await folderPairRepository.GetByJobIdAsync(job.Id))
        {
            FolderPairs.Add(pair);
        }

        if (job.JobRobocopyOptionSetId is { } optionSetId)
        {
            // Distinguishes "still fetching the existing override" from "user already
            // toggled it off in memory" for ToggleJobOptionOverridesAsync's race guard below —
            // both would otherwise look identical (JobOptionOverrides null, but the job's FK
            // still set, since only Save clears the FK). try/finally so a failure partway
            // through (e.g. the app-level default RobocopyOptionSet row is missing) can't leave
            // this stuck true forever, permanently no-opping the toggle command with no error.
            _isLoadingJobOptionOverrides = true;
            try
            {
                var jobOverrides = await optionSetRepository.GetByIdAsync(optionSetId);

                // Rows created before checkbox materialization existed (or edited directly in
                // the DB) can still have null boolean fields, which would render as an ambiguous
                // indeterminate checkbox even though IsThreeState is off. Backfill BEFORE
                // assigning to the bound property, same reasoning as SettingsViewModel's load path.
                if (jobOverrides is not null)
                {
                    var appDefaults = await optionSetRepository.GetRequiredDefaultAsync(appSettingsCache);
                    RobocopyOptionsResolver.BackfillNullBooleans(jobOverrides, appDefaults);
                }

                JobOptionOverrides = jobOverrides;
            }
            finally
            {
                _isLoadingJobOptionOverrides = false;
            }
        }
    }

    [RelayCommand]
    private async Task AddJobAsync()
    {
        var job = new Job
        {
            Id = Guid.NewGuid().ToString(),
            Name = $"New Job {Jobs.Count + 1}",
            SortOrder = Jobs.Count,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow,
        };

        try
        {
            await jobRepository.InsertAsync(job);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            // The auto-generated name (based on the current active job count) can collide with
            // an active job that was renamed to match it — same UX_Job_Name_WhenActive
            // constraint SaveJobAsync already handles for its own rename path.
            StatusMessage = $"Couldn't add job: another active job is already named '{job.Name}'. Rename it and try again.";
            return;
        }

        Jobs.Add(job);
        SelectedJob = job;
    }

    [RelayCommand]
    private async Task DeleteJobAsync()
    {
        if (SelectedJob is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"Delete job '{SelectedJob.Name}' and all its folder pairs? Its run history is kept " +
                "as an audit trail (see the 'Job referenced' column in Run History). This cannot be undone.",
                "Delete job", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        // Soft-deleting the job doesn't stop it from being scheduled — without this, its
        // Windows Task Scheduler task would keep existing and firing forever (each run just
        // logging "skipping" and exiting, since the headless runner checks IsDeleted, but
        // left behind as orphaned clutter in Task Scheduler otherwise). Same cleanup
        // ScheduleEditorViewModel.RemoveScheduleAsync already does for an explicit removal.
        var scheduleMetadata = await scheduleMetadataRepository.GetByJobIdAsync(SelectedJob.Id);
        if (scheduleMetadata is not null)
        {
            taskSchedulerService.DeleteTask(scheduleMetadata.TaskSchedulerTaskName);
            await scheduleMetadataRepository.DeleteAsync(scheduleMetadata.Id);
        }

        await jobRepository.DeleteAsync(SelectedJob.Id);
        Jobs.Remove(SelectedJob);
        SelectedJob = null;
    }

    [RelayCommand(CanExecute = nameof(CanSaveJob))]
    private async Task SaveJobAsync()
    {
        if (SelectedJob is null)
        {
            return;
        }

        try
        {
            string? removedOptionSetId = null;
            if (JobOptionOverrides is not null)
            {
                if (SelectedJob.JobRobocopyOptionSetId is null)
                {
                    await optionSetRepository.InsertAsync(JobOptionOverrides);
                    SelectedJob.JobRobocopyOptionSetId = JobOptionOverrides.Id;
                }
                else
                {
                    await optionSetRepository.UpdateAsync(JobOptionOverrides);
                }
            }
            else if (SelectedJob.JobRobocopyOptionSetId is { } existingOptionSetId)
            {
                // The user toggled overrides off for a job that previously had them — clear the
                // FK now, but defer actually deleting the now-orphaned row until after the Job
                // itself is persisted below: SQLite's foreign key check runs at DELETE time, and
                // would reject removing a row the database's own copy of this job still points
                // to (same fix as FolderPairEditorViewModel.SaveAsync).
                removedOptionSetId = existingOptionSetId;
                SelectedJob.JobRobocopyOptionSetId = null;
            }

            SelectedJob.UpdatedUtc = DateTime.UtcNow;
            await jobRepository.UpdateAsync(SelectedJob);

            if (removedOptionSetId is not null)
            {
                await optionSetRepository.DeleteAsync(removedOptionSetId);
            }

            StatusMessage = $"Saved '{SelectedJob.Name}' at {DateTime.Now:T}.";
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19) // SQLITE_CONSTRAINT
        {
            // Name is the only uniqueness constraint on Job (scoped to active jobs — see
            // migration comment on UX_Job_Name_WhenActive), so a constraint failure here always
            // means the new name collides with another active job's.
            StatusMessage = $"Couldn't save: another active job is already named '{SelectedJob.Name}'.";
        }
    }

    private bool CanSaveJob() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSaveJob))]
    private async Task ToggleJobOptionOverridesAsync()
    {
        if (JobOptionOverrides is not null)
        {
            JobOptionOverrides = null;
            return;
        }

        // LoadFolderPairsAsync is fire-and-forget from OnSelectedJobChanged and is still
        // resolving the job's existing override — ignore this click rather than race it and
        // clobber whichever assignment finishes last. Can't simply check the job's FK here:
        // once the user has toggled overrides off in memory (the branch above), the FK is
        // still set until Save runs, which would otherwise make "still loading" and "already
        // disabled, ready to re-enable" indistinguishable.
        if (_isLoadingJobOptionOverrides)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var appDefaults = await optionSetRepository.GetRequiredDefaultAsync(appSettingsCache);
            JobOptionOverrides = RobocopyOptionsResolver.CreateMaterializedOverride(Guid.NewGuid().ToString(), appDefaults);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't enable job-level overrides: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddFolderPairAsync()
    {
        if (SelectedJob is null)
        {
            return;
        }

        var pair = new FolderPair { Id = Guid.NewGuid().ToString(), JobId = SelectedJob.Id, SourcePath = "", DestinationPath = "" };
        await EditFolderPairAsync(pair, isNew: true);
    }

    [RelayCommand]
    private async Task EditSelectedFolderPairAsync()
    {
        if (SelectedFolderPair is not null)
        {
            await EditFolderPairAsync(SelectedFolderPair, isNew: false);
        }
    }

    private async Task EditFolderPairAsync(FolderPair pair, bool isNew)
    {
        var editorViewModel = new FolderPairEditorViewModel(
            folderPairRepository, optionSetRepository, exclusionRuleRepository, verificationSettingsRepository,
            appSettingsCache, JobOptionOverrides, pair, isNew, credentialTargetsViewModel.Targets);

        var window = new FolderPairEditorWindow { DataContext = editorViewModel, Owner = Application.Current.MainWindow };
        if (window.ShowDialog() == true)
        {
            await LoadFolderPairsAsync(SelectedJob);
        }
    }

    [RelayCommand]
    private async Task DeleteFolderPairAsync()
    {
        if (SelectedFolderPair is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"Delete folder pair '{SelectedFolderPair.SourcePath} -> {SelectedFolderPair.DestinationPath}'? Its run " +
                "history is kept as an audit trail (see the 'Pair referenced' column in Run History). This cannot be undone.",
                "Delete folder pair", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await folderPairRepository.DeleteAsync(SelectedFolderPair.Id);
        FolderPairs.Remove(SelectedFolderPair);
        SelectedFolderPair = null;
    }

    /// <summary>
    /// Persists a folder pair immediately after an inline grid-cell edit (currently just the
    /// Enabled checkbox — the other columns are read-only) — there's no separate "Save" step
    /// for that quick-toggle interaction the way there is for the full pair editor dialog.
    /// </summary>
    public async Task SaveFolderPairAsync(FolderPair pair) => await folderPairRepository.UpdateAsync(pair);

    private bool CanRun() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunJobAsync() => await ExecuteAsync(FolderPairs.Where(p => p.IsEnabled).ToList(), dryRun: false, RunTriggerType.Manual);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task DryRunJobAsync() => await ExecuteAsync(FolderPairs.Where(p => p.IsEnabled).ToList(), dryRun: true, RunTriggerType.Manual);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunSelectedPairAsync()
    {
        if (SelectedFolderPair is not null)
        {
            await ExecuteAsync([SelectedFolderPair], dryRun: false, RunTriggerType.Manual);
        }
    }

    private async Task ExecuteAsync(IReadOnlyList<FolderPair> pairs, bool dryRun, RunTriggerType triggerType)
    {
        // Defensive re-check even though the commands are already CanExecute-gated on !IsBusy:
        // a run could still be in flight here if this is ever invoked from a non-command path
        // (e.g. a future scheduled/headless caller) that doesn't go through the gated commands.
        if (IsBusy || SelectedJob is null)
        {
            return;
        }

        // The headless/scheduled runner (Program.cs) already refuses to run a disabled job —
        // the interactive Run job/Dry run/Run this pair buttons must match that, or unticking
        // "Enabled" here does nothing for a manual run.
        if (!SelectedJob.IsEnabled)
        {
            StatusMessage = $"'{SelectedJob.Name}' is disabled — enable it first to run it.";
            return;
        }

        // Checked after IsEnabled (rather than alongside the guard above) so a disabled job
        // always reports itself as the blocker first — otherwise "Run job" on a disabled job
        // with all-disabled pairs would report the less relevant of the two problems.
        if (pairs.Count == 0)
        {
            StatusMessage = $"'{SelectedJob.Name}' has no enabled folder pairs to run.";
            return;
        }

        var appDefaults = await optionSetRepository.GetRequiredDefaultAsync(appSettingsCache);
        var jobOverrides = SelectedJob.JobRobocopyOptionSetId is { } jobOptionSetId
            ? await optionSetRepository.GetByIdAsync(jobOptionSetId) : null;

        var resolvedByPair = new List<(FolderPair Pair, ResolvedRobocopyOptions Options)>();
        foreach (var pair in pairs)
        {
            var pairOverrides = pair.PairRobocopyOptionSetId is { } pairOptionSetId
                ? await optionSetRepository.GetByIdAsync(pairOptionSetId) : null;
            resolvedByPair.Add((pair, RobocopyOptionsResolver.Resolve(appDefaults, jobOverrides, pairOverrides)));
        }

        if (!dryRun && !DestructiveRunConfirmation.ConfirmIfNeeded(resolvedByPair))
        {
            StatusMessage = "Run cancelled.";
            return;
        }

        IsBusy = true;
        RunProgressText = null;
        StatusMessage = dryRun ? "Running dry run..." : "Running...";
        _runCancellationTokenSource = new CancellationTokenSource();
        var liveCopyProgress = new Progress<int>(count => RunProgressText = $"{count} file(s) processed so far");
        var verificationProgress = new Progress<VerificationProgress>(vp => RunProgressText = vp.FilesCompleted > 0
            ? $"{vp.PhaseMessage} {vp.FilesCompleted} file(s) processed so far."
            : vp.PhaseMessage);
        try
        {
            var runHistory = await jobExecutionService.RunJobAsync(
                SelectedJob, pairs, triggerType, dryRun, _runCancellationTokenSource.Token, liveCopyProgress, verificationProgress);
            StatusMessage = $"{(dryRun ? "Dry run" : "Run")} finished: {runHistory.OverallOutcome}.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Run cancelled.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Run failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            RunProgressText = null;
            _runCancellationTokenSource.Dispose();
            _runCancellationTokenSource = null;
        }
    }
}
