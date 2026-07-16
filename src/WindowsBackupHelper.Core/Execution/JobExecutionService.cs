using WindowsBackupHelper.Core.Credentials;
using WindowsBackupHelper.Core.Exclusions;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Notifications;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Robocopy;
using WindowsBackupHelper.Core.Smb;

namespace WindowsBackupHelper.Core.Execution;

/// <summary>A phase-labeled progress update during post-copy verification (checksum
/// generate/verify/update, FLAC audit) -- PhaseMessage identifies which of those is currently
/// running, FilesCompleted is how many files that phase has processed so far.</summary>
public readonly record struct VerificationProgress(string PhaseMessage, int FilesCompleted);

/// <summary>
/// Wires the full per-job pipeline: resolve options -> resolve exclusions -> connect
/// credentials -> spawn robocopy -> parse/persist results -> optionally checksum/FLAC ->
/// notify. One RunJobAsync call produces one RunHistory row; a whole-job run produces N
/// child FolderPairRunResult rows (one per pair), a single-pair run produces exactly 1.
///
/// This intentionally does NOT show any confirmation UI for destructive flags (/MIR, /PURGE,
/// /MOVE) — that gate belongs to the interactive caller (the WPF layer), since a headless
/// scheduled run has nobody to confirm with.
/// </summary>
public sealed class JobExecutionService(
    RobocopyOptionSetRepository optionSetRepository,
    ExclusionRuleRepository exclusionRuleRepository,
    CredentialTargetRepository credentialTargetRepository,
    VerificationSettingsRepository verificationSettingsRepository,
    AppSettingsRepository appSettingsRepository,
    RunHistoryRepository runHistoryRepository,
    FolderPairRunResultRepository runResultRepository,
    ICredentialStore credentialStore,
    ISmbConnectionManager smbConnectionManager,
    IRobocopyProcessRunner robocopyProcessRunner,
    IFileSystemEnumerator fileSystemEnumerator,
    VerificationRunner verificationRunner,
    INotificationService notificationService,
    string logDirectory)
{
    public async Task<RunHistory> RunJobAsync(
        Job job,
        IReadOnlyList<FolderPair> folderPairs,
        RunTriggerType triggerType,
        bool dryRun,
        CancellationToken cancellationToken = default,
        IProgress<int>? liveFilesCopiedProgress = null,
        IProgress<VerificationProgress>? verificationProgress = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(folderPairs);

        var runHistory = new RunHistory
        {
            JobId = job.Id,
            TriggerType = triggerType,
            StartedUtc = DateTime.UtcNow,
            WasDryRun = dryRun,
        };
        runHistory.Id = await runHistoryRepository.InsertAsync(runHistory).ConfigureAwait(false);

        var results = new List<FolderPairRunResult>();
        foreach (var pair in folderPairs.Where(p => p.IsEnabled))
        {
            FolderPairRunResult result;
            try
            {
                result = await RunFolderPairAsync(job, pair, runHistory.Id, dryRun, cancellationToken, liveFilesCopiedProgress, verificationProgress).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                result = new FolderPairRunResult
                {
                    RunHistoryId = runHistory.Id,
                    FolderPairId = pair.Id,
                    StartedUtc = DateTime.UtcNow,
                    CompletedUtc = DateTime.UtcNow,
                    ErrorMessage = ex.Message,
                };
                result.Id = await runResultRepository.InsertAsync(result).ConfigureAwait(false);
            }

            results.Add(result);
        }

        runHistory.CompletedUtc = DateTime.UtcNow;
        runHistory.OverallOutcome = AggregateOutcome(results);
        await runHistoryRepository.UpdateAsync(runHistory).ConfigureAwait(false);

        var isSuccess = runHistory.OverallOutcome is RunOutcome.Success or RunOutcome.SuccessWithMismatches;
        notificationService.NotifyJobCompleted(job.Name, isSuccess, runHistory.OverallOutcome.ToString() ?? "Unknown");

        return runHistory;
    }

    public async Task<FolderPairRunResult> RunFolderPairAsync(
        Job job, FolderPair pair, int runHistoryId, bool dryRun,
        CancellationToken cancellationToken = default, IProgress<int>? liveFilesCopiedProgress = null,
        IProgress<VerificationProgress>? verificationProgress = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(pair);

        var startedUtc = DateTime.UtcNow;
        var warnings = new List<string>();

        // 1. Resolve options: AppSettings -> Job -> FolderPair cascade.
        var appSettings = await appSettingsRepository.GetAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("AppSettings has not been initialized.");
        var appDefaults = await optionSetRepository.GetByIdAsync(appSettings.DefaultRobocopyOptionSetId).ConfigureAwait(false)
            ?? throw new InvalidOperationException("AppSettings' default RobocopyOptionSet is missing.");
        var jobOverrides = job.JobRobocopyOptionSetId is { } jobOptionSetId
            ? await optionSetRepository.GetByIdAsync(jobOptionSetId).ConfigureAwait(false)
            : null;
        var pairOverrides = pair.PairRobocopyOptionSetId is { } pairOptionSetId
            ? await optionSetRepository.GetByIdAsync(pairOptionSetId).ConfigureAwait(false)
            : null;
        var resolvedOptions = RobocopyOptionsResolver.Resolve(appDefaults, jobOverrides, pairOverrides);

        // 2. Connect credentials (before exclusion resolution, since resolving path-scoped
        //    wildcard/regex rules may need to enumerate the live source tree).
        using var sourceConnection = await ConnectIfNeededAsync(pair.SourcePath, pair.SourceCredentialTargetId, cancellationToken).ConfigureAwait(false);
        using var destinationConnection = await ConnectIfNeededAsync(pair.DestinationPath, pair.DestinationCredentialTargetId, cancellationToken).ConfigureAwait(false);

        // 3. Resolve exclusions.
        var applicableRules = await exclusionRuleRepository.GetApplicableRulesAsync(job.Id, pair.Id).ConfigureAwait(false);
        var exclusionResult = new ExclusionRuleResolver(fileSystemEnumerator).Resolve(applicableRules, pair.SourcePath);
        warnings.AddRange(exclusionResult.Warnings);

        // 4. Spawn robocopy.
        var logPath = Path.Combine(logDirectory, job.Id, $"{runHistoryId}_{pair.Id}.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
        var commandLine = RobocopyArgumentBuilder.Build(
            resolvedOptions, pair.SourcePath, pair.DestinationPath,
            exclusionResult.ExcludeFileTokens, exclusionResult.ExcludeDirectoryTokens, logPath, dryRun);
        warnings.AddRange(commandLine.Warnings);

        RobocopyProcessResult processResult;
        if (liveFilesCopiedProgress is null)
        {
            processResult = await robocopyProcessRunner.RunAsync(commandLine.Arguments, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            using var tailCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var tailTask = TailLogForLiveProgressAsync(logPath, liveFilesCopiedProgress, tailCts.Token);
            try
            {
                processResult = await robocopyProcessRunner.RunAsync(commandLine.Arguments, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                tailCts.Cancel();
                await tailTask.ConfigureAwait(false);
            }
        }

        // 5. Parse/persist. /UNILOG(+) redirects Robocopy's own output to the log file
        // instead of the console, so the summary block must be read from there rather than
        // from standard output, which is empty. Deliberately no explicit Encoding here: /UNILOG
        // (fresh file) writes UTF-16LE with a BOM, but /UNILOG+ (append) writing to a
        // not-yet-existing file — which every run hits, since each run gets its own unique log
        // path — has been observed to fall back to the system's single-byte ANSI/OEM encoding
        // instead, with no BOM at all. Hardcoding Encoding.Unicode silently corrupted every
        // character in that case (each byte pair decoded as one wrong UTF-16 code unit),
        // leaving the whole summary block unparseable. Omitting the encoding lets
        // File.ReadAllTextAsync auto-detect via BOM (UTF-16LE/BE/UTF-8) and fall back to UTF-8
        // — which decodes plain ASCII identically to ANSI — when none is present.
        var summaryText = File.Exists(logPath)
            ? await File.ReadAllTextAsync(logPath, cancellationToken).ConfigureAwait(false)
            : processResult.StandardOutput;
        var summary = RobocopyOutputParser.Parse(summaryText);
        var outcome = RobocopyExitCodeInterpreter.Interpret(processResult.ExitCode);
        var outcomeSummary = warnings.Count > 0
            ? $"{outcome.HumanReadableSummary} Warnings: {string.Join(' ', warnings)}"
            : outcome.HumanReadableSummary;

        var result = new FolderPairRunResult
        {
            RunHistoryId = runHistoryId,
            FolderPairId = pair.Id,
            StartedUtc = startedUtc,
            CompletedUtc = DateTime.UtcNow,
            RobocopyExitCode = processResult.ExitCode,
            RobocopyOutcomeSummary = outcomeSummary,
            DirsCopied = (int?)summary.Dirs?.Copied,
            DirsSkipped = (int?)summary.Dirs?.Skipped,
            DirsExtras = (int?)summary.Dirs?.Extras,
            DirsFailed = (int?)summary.Dirs?.Failed,
            DirsMismatch = (int?)summary.Dirs?.Mismatch,
            FilesCopied = (int?)summary.Files?.Copied,
            FilesSkipped = (int?)summary.Files?.Skipped,
            FilesExtras = (int?)summary.Files?.Extras,
            FilesFailed = (int?)summary.Files?.Failed,
            FilesMismatch = (int?)summary.Files?.Mismatch,
            BytesCopied = summary.Bytes?.Copied,
            AverageSpeedBytesPerSec = summary.AverageSpeedBytesPerSec,
            RobocopyLogFilePath = logPath,
        };

        // 6. Optionally checksum/FLAC the destination — skipped entirely for dry runs, since
        // /L means the destination wasn't actually touched.
        if (!dryRun && pair.VerificationSettingsId is { } verificationSettingsId)
        {
            var verificationSettings = await verificationSettingsRepository.GetByIdAsync(verificationSettingsId).ConfigureAwait(false);
            if (verificationSettings is not null)
            {
                result = await ApplyVerificationAsync(result, pair, verificationSettings, cancellationToken, verificationProgress).ConfigureAwait(false);
            }
        }

        result.Id = await runResultRepository.InsertAsync(result).ConfigureAwait(false);
        return result;
    }

    private async Task<FolderPairRunResult> ApplyVerificationAsync(
        FolderPairRunResult result, FolderPair pair, VerificationSettings settings, CancellationToken cancellationToken,
        IProgress<VerificationProgress>? verificationProgress)
    {
        var verificationResult = await verificationRunner
            .RunAsync(pair.DestinationPath, settings, cancellationToken, verificationProgress).ConfigureAwait(false);

        result.ChecksumOutcomeSummary = verificationResult.ChecksumOutcomeSummary;
        result.ChecksumManifestPath = verificationResult.ChecksumManifestPath;
        result.ChecksumReportPath = verificationResult.ChecksumReportPath;
        result.ChecksumHasIssues = verificationResult.ChecksumHasIssues;
        result.FlacOutcomeSummary = verificationResult.FlacOutcomeSummary;
        result.FlacReportPath = verificationResult.FlacReportPath;
        result.FlacHasIssues = verificationResult.FlacHasIssues;

        return result;
    }

    /// <summary>
    /// Polls the growing /UNILOG file while Robocopy is still running and reports a live
    /// "files completed so far" count — an approximate, UI-only signal; the persisted
    /// Files/Bytes-copied totals always come from RobocopyOutputParser's authoritative final
    /// summary block once the run finishes, independent of this. Never throws: cancellation
    /// (requested once the real robocopy invocation completes) just ends the loop quietly, so
    /// the caller can safely await this task in a finally block.
    /// </summary>
    private static async Task TailLogForLiveProgressAsync(string logPath, IProgress<int> progress, CancellationToken cancellationToken)
    {
        var lastReported = -1;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(logPath))
                {
                    using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream);
                    var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    var count = RobocopyLiveProgressCounter.CountCompletedFiles(content);
                    if (count != lastReported)
                    {
                        lastReported = count;
                        progress.Report(count);
                    }
                }
            }
            catch (IOException)
            {
                // Log file may be momentarily locked mid-write by Robocopy; just retry next tick.
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task<IDisposable?> ConnectIfNeededAsync(string path, string? credentialTargetId, CancellationToken cancellationToken)
    {
        if (credentialTargetId is null || !path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return null;
        }

        var credentialTarget = await credentialTargetRepository.GetByIdAsync(credentialTargetId).ConfigureAwait(false);
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

    private static RunOutcome AggregateOutcome(IReadOnlyList<FolderPairRunResult> results)
    {
        if (results.Count == 0)
        {
            return RunOutcome.Success;
        }

        var anySuccess = results.Any(r => r.RobocopyExitCode is { } code && RobocopyExitCodeInterpreter.Interpret(code).IsSuccess);
        var anyFailure = results.Any(r => r.RobocopyExitCode is not { } code || !RobocopyExitCodeInterpreter.Interpret(code).IsSuccess);

        if (anyFailure && anySuccess)
        {
            return RunOutcome.PartialFailure;
        }

        if (anyFailure)
        {
            return RunOutcome.Failed;
        }

        var anyMismatch = results.Any(r =>
            (r.FilesMismatch is > 0) ||
            (r.DirsMismatch is > 0) ||
            r.ChecksumHasIssues == true ||
            r.FlacHasIssues == true);

        return anyMismatch ? RunOutcome.SuccessWithMismatches : RunOutcome.Success;
    }
}
