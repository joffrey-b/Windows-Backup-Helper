using WindowsBackupHelper.Core.Checksums;
using WindowsBackupHelper.Core.Exclusions;
using WindowsBackupHelper.Core.Flac;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Execution;

/// <summary>The outcome of a verification run: checksum summary/manifest/report paths (if
/// checksumming was configured) and FLAC summary/report path (if the audit was configured).
/// Any of these can be null -- VerificationSettings.ChecksumMode/RunFlacAudit each opt in
/// independently. ChecksumHasIssues/FlacHasIssues are structured pass/fail signals (changed or
/// missing files, read errors, FLAC errors) for callers that need to know whether verification
/// actually found a problem, rather than re-parsing the human-readable summary strings.</summary>
public sealed record VerificationRunResult(
    string? ChecksumOutcomeSummary,
    string? ChecksumManifestPath,
    string? ChecksumReportPath,
    string? FlacOutcomeSummary,
    string? FlacReportPath,
    bool ChecksumHasIssues = false,
    bool FlacHasIssues = false);

/// <summary>
/// Runs checksum generate/verify/update and/or a FLAC audit against a folder, per
/// VerificationSettings. Shared by two callers: JobExecutionService (verification immediately
/// after a Robocopy copy, as part of a job) and the standalone Verify Backup tab (an ad-hoc,
/// periodic check of a folder that was never touched by a job in this run) -- both need the
/// exact same generate/verify/update/audit + progress + report-writing behavior, just against
/// a different notion of "which folder" and "what to do with the result."
/// </summary>
public sealed class VerificationRunner(
    IFileSystemEnumerator fileSystemEnumerator,
    IFileHasher fileHasher,
    IFlacProcessRunner flacProcessRunner)
{
    public async Task<VerificationRunResult> RunAsync(
        string folderPath, VerificationSettings settings, CancellationToken cancellationToken = default,
        IProgress<VerificationProgress>? verificationProgress = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);
        ArgumentNullException.ThrowIfNull(settings);

        string? checksumOutcomeSummary = null;
        string? checksumManifestPath = null;
        string? checksumReportPath = null;
        string? flacOutcomeSummary = null;
        string? flacReportPath = null;
        var checksumHasIssues = false;
        var flacHasIssues = false;

        if (settings.ChecksumMode != ChecksumMode.None)
        {
            var manifestPath = settings.ChecksumManifestPath;
            if (string.IsNullOrWhiteSpace(manifestPath))
            {
                // Verify/Update used to silently fall through to Generate when this was blank --
                // and since Generate's own manifest write is itself gated on a non-blank path,
                // that meant hashing the whole folder for nothing: no manifest, no persisted
                // result, no indication anything was skipped. Fail loudly instead.
                throw new InvalidOperationException(
                    $"Checksum mode '{settings.ChecksumMode}' requires a manifest path, but none was set.");
            }

            var workers = settings.ChecksumWorkers ?? 4;
            var reportPath = settings.ChecksumReportOutputPath;

            if (settings.ChecksumMode == ChecksumMode.Generate)
            {
                var phaseProgress = CreatePhaseProgress("Generating checksums. This may take a while.", verificationProgress);
                var checksumStart = DateTime.UtcNow;
                var generateResult = await new ChecksumGenerateService(fileSystemEnumerator, fileHasher)
                    .GenerateAsync(folderPath, workers, cancellationToken, phaseProgress).ConfigureAwait(false);
                var elapsed = DateTime.UtcNow - checksumStart;
                ChecksumManifest.Write(generateResult.Entries, manifestPath);

                checksumOutcomeSummary = $"Generated {generateResult.Entries.Count} checksum(s), {generateResult.Errors.Count} error(s).";
                checksumManifestPath = manifestPath;
                checksumHasIssues = generateResult.Errors.Count > 0;

                if (!string.IsNullOrWhiteSpace(reportPath))
                {
                    var markdown = ChecksumReportWriter.GenerateMarkdownForGenerate(generateResult, folderPath, elapsed);
                    ChecksumReportWriter.WriteToFile(markdown, reportPath);
                    checksumReportPath = reportPath;
                }
            }
            else if (settings.ChecksumMode == ChecksumMode.VerifyAgainstManifest)
            {
                var phaseProgress = CreatePhaseProgress("Verifying checksums. This may take a while.", verificationProgress);
                var checksumStart = DateTime.UtcNow;
                var manifest = ChecksumManifest.Read(manifestPath);
                var verifyResult = await new ChecksumVerifyService(fileHasher)
                    .VerifyAsync(folderPath, manifest, workers, cancellationToken, phaseProgress).ConfigureAwait(false);
                var elapsed = DateTime.UtcNow - checksumStart;
                checksumOutcomeSummary =
                    $"OK: {verifyResult.Ok.Count}, Changed: {verifyResult.Changed.Count}, " +
                    $"Missing: {verifyResult.Missing.Count}, Read errors: {verifyResult.ReadErrors.Count}.";
                checksumManifestPath = manifestPath;
                checksumHasIssues = verifyResult.Changed.Count > 0 || verifyResult.Missing.Count > 0 || verifyResult.ReadErrors.Count > 0;

                if (!string.IsNullOrWhiteSpace(reportPath))
                {
                    var markdown = ChecksumReportWriter.GenerateMarkdownForVerify(verifyResult, folderPath, elapsed);
                    ChecksumReportWriter.WriteToFile(markdown, reportPath);
                    checksumReportPath = reportPath;
                }
            }
            else if (settings.ChecksumMode == ChecksumMode.Update)
            {
                var phaseProgress = CreatePhaseProgress("Updating checksums. This may take a while.", verificationProgress);
                var checksumStart = DateTime.UtcNow;
                var manifest = ChecksumManifest.Read(manifestPath);
                var updateResult = await new ChecksumUpdateService(fileSystemEnumerator, fileHasher)
                    .UpdateAsync(folderPath, manifest, workers, cancellationToken, phaseProgress).ConfigureAwait(false);
                var elapsed = DateTime.UtcNow - checksumStart;
                ChecksumManifest.Write(updateResult.UpdatedEntries, manifestPath);
                checksumOutcomeSummary = $"Added {updateResult.Added.Count}, removed {updateResult.Removed.Count}.";
                checksumManifestPath = manifestPath;
                checksumHasIssues = updateResult.Errors.Count > 0;

                if (!string.IsNullOrWhiteSpace(reportPath))
                {
                    var markdown = ChecksumReportWriter.GenerateMarkdownForUpdate(updateResult, folderPath, elapsed);
                    ChecksumReportWriter.WriteToFile(markdown, reportPath);
                    checksumReportPath = reportPath;
                }
            }
        }

        if (settings.RunFlacAudit)
        {
            var flacWorkers = settings.FlacWorkers ?? Math.Min(8, Environment.ProcessorCount);
            var flacStart = DateTime.UtcNow;
            var flacPhaseProgress = CreatePhaseProgress("Flac audit running. This may take a while.", verificationProgress);
            var flacResults = await new FlacAuditService(fileSystemEnumerator, flacProcessRunner)
                .RunAsync(folderPath, flacWorkers, cancellationToken, flacPhaseProgress).ConfigureAwait(false);
            var elapsed = DateTime.UtcNow - flacStart;

            var errorCount = flacResults.Count(r => r.Status == FlacFileStatus.Error);
            var warningCount = flacResults.Count(r => r.Status == FlacFileStatus.Warning);
            flacOutcomeSummary = $"{flacResults.Count} file(s): {errorCount} error(s), {warningCount} warning(s).";
            flacHasIssues = errorCount > 0;

            if (!string.IsNullOrWhiteSpace(settings.FlacReportOutputPath))
            {
                var markdown = FlacAuditReportWriter.GenerateMarkdown(
                    flacResults, folderPath, elapsed, settings.FlacErrorsOnly);
                FlacAuditReportWriter.WriteToFile(markdown, settings.FlacReportOutputPath);
                flacReportPath = settings.FlacReportOutputPath;
            }
        }

        return new VerificationRunResult(
            checksumOutcomeSummary, checksumManifestPath, checksumReportPath, flacOutcomeSummary, flacReportPath,
            checksumHasIssues, flacHasIssues);
    }

    /// <summary>
    /// Wraps verificationProgress into a plain IProgress&lt;int&gt; labeled with the given phase,
    /// reporting once immediately (at 0) so the UI shows the phase message right away rather
    /// than waiting for the first file to finish -- which can take a while for a large or
    /// slow-over-SMB first file.
    /// </summary>
    private static IProgress<int>? CreatePhaseProgress(string phaseMessage, IProgress<VerificationProgress>? verificationProgress)
    {
        if (verificationProgress is null)
        {
            return null;
        }

        verificationProgress.Report(new VerificationProgress(phaseMessage, 0));
        return new Progress<int>(count => verificationProgress.Report(new VerificationProgress(phaseMessage, count)));
    }
}
