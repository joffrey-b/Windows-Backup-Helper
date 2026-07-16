using WindowsBackupHelper.Core.Checksums;
using WindowsBackupHelper.Core.Exclusions;
using WindowsBackupHelper.Core.Execution;
using WindowsBackupHelper.Core.Flac;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Tests.Checksums;
using WindowsBackupHelper.Core.Tests.Exclusions;
using WindowsBackupHelper.Core.Tests.Flac;

namespace WindowsBackupHelper.Core.Tests.Execution;

public sealed class VerificationRunnerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"wbh-verification-runner-{Guid.NewGuid():N}");

    public VerificationRunnerTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static VerificationSettings NewSettings() => new() { Id = Guid.NewGuid().ToString() };

    [Fact]
    public async Task RunAsync_GenerateMode_WritesManifestAndReturnsSummary()
    {
        var manifestPath = Path.Combine(_root, "manifest.sha256");
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("a.txt", false));
        var hasher = new FakeFileHasher(new Dictionary<string, string> { [ChecksumFileDiscovery.ToAbsolutePath(_root, "a.txt")] = "digest" });
        var runner = new VerificationRunner(enumerator, hasher, new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>()));
        var settings = NewSettings();
        settings.ChecksumMode = ChecksumMode.Generate;
        settings.ChecksumManifestPath = manifestPath;

        var result = await runner.RunAsync(_root, settings);

        Assert.Contains("Generated 1 checksum", result.ChecksumOutcomeSummary);
        Assert.True(File.Exists(manifestPath));
        Assert.Null(result.FlacOutcomeSummary);
    }

    [Fact]
    public async Task RunAsync_VerifyMode_ReturnsOkCount()
    {
        var manifestPath = Path.Combine(_root, "manifest.sha256");
        ChecksumManifest.Write(new Dictionary<string, string> { ["a.txt"] = "digest" }, manifestPath);
        File.WriteAllText(Path.Combine(_root, "a.txt"), "content");
        var hasher = new FakeFileHasher(new Dictionary<string, string> { [Path.Combine(_root, "a.txt")] = "digest" });
        var runner = new VerificationRunner(new FakeFileSystemEnumerator(), hasher, new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>()));
        var settings = NewSettings();
        settings.ChecksumMode = ChecksumMode.VerifyAgainstManifest;
        settings.ChecksumManifestPath = manifestPath;

        var result = await runner.RunAsync(_root, settings);

        Assert.Contains("OK: 1", result.ChecksumOutcomeSummary);
        Assert.False(result.ChecksumHasIssues);
    }

    [Fact]
    public async Task RunAsync_VerifyMode_FileMissingFromDisk_SetsChecksumHasIssuesTrue()
    {
        // Regression test: AggregateOutcome used to decide pass/fail by substring-matching
        // ChecksumOutcomeSummary for "Changed:", which a Missing-only result (like this one)
        // would never trip, silently reporting a backup with genuinely missing files as healthy.
        var manifestPath = Path.Combine(_root, "manifest.sha256");
        ChecksumManifest.Write(new Dictionary<string, string> { ["gone.txt"] = "digest" }, manifestPath);
        var runner = new VerificationRunner(new FakeFileSystemEnumerator(), new FakeFileHasher(), new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>()));
        var settings = NewSettings();
        settings.ChecksumMode = ChecksumMode.VerifyAgainstManifest;
        settings.ChecksumManifestPath = manifestPath;

        var result = await runner.RunAsync(_root, settings);

        Assert.Contains("Missing: 1", result.ChecksumOutcomeSummary);
        Assert.True(result.ChecksumHasIssues);
    }

    [Fact]
    public async Task RunAsync_WithChecksumReportPath_WritesReportAndReturnsItsPath()
    {
        var manifestPath = Path.Combine(_root, "manifest.sha256");
        var reportPath = Path.Combine(_root, "report.md");
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("a.txt", false));
        var hasher = new FakeFileHasher(new Dictionary<string, string> { [ChecksumFileDiscovery.ToAbsolutePath(_root, "a.txt")] = "digest" });
        var runner = new VerificationRunner(enumerator, hasher, new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>()));
        var settings = NewSettings();
        settings.ChecksumMode = ChecksumMode.Generate;
        settings.ChecksumManifestPath = manifestPath;
        settings.ChecksumReportOutputPath = reportPath;

        var result = await runner.RunAsync(_root, settings);

        Assert.Equal(reportPath, result.ChecksumReportPath);
        Assert.True(File.Exists(reportPath));
        Assert.Contains("# Checksum Generation Report", File.ReadAllText(reportPath));
    }

    [Fact]
    public async Task RunAsync_FlacAuditEnabled_ReturnsSummaryAndWritesReport()
    {
        var reportPath = Path.Combine(_root, "flac-report.md");
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("track.flac", false));
        var flacRunner = new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>());
        var runner = new VerificationRunner(enumerator, new FakeFileHasher(), flacRunner);
        var settings = NewSettings();
        settings.RunFlacAudit = true;
        settings.FlacReportOutputPath = reportPath;

        var result = await runner.RunAsync(_root, settings);

        Assert.Contains("1 file(s)", result.FlacOutcomeSummary);
        Assert.Equal(reportPath, result.FlacReportPath);
        Assert.True(File.Exists(reportPath));
        Assert.Null(result.ChecksumOutcomeSummary);
        Assert.False(result.FlacHasIssues);
    }

    [Fact]
    public async Task RunAsync_FlacAuditEnabled_WithCorruptFile_SetsFlacHasIssuesTrue()
    {
        // Regression test: AggregateOutcome used to never consult FlacOutcomeSummary at all, so
        // a FLAC audit finding corrupt files had zero effect on the job's reported outcome.
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("bad.flac", false));
        var flacRunner = new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>
        {
            [Path.Combine(_root, "bad.flac")] = new(1, "", "ERROR: corrupt"),
        });
        var runner = new VerificationRunner(enumerator, new FakeFileHasher(), flacRunner);
        var settings = NewSettings();
        settings.RunFlacAudit = true;

        var result = await runner.RunAsync(_root, settings);

        Assert.Contains("1 error(s)", result.FlacOutcomeSummary);
        Assert.True(result.FlacHasIssues);
    }

    [Theory]
    [InlineData(ChecksumMode.Generate)]
    [InlineData(ChecksumMode.VerifyAgainstManifest)]
    [InlineData(ChecksumMode.Update)]
    public async Task RunAsync_ChecksumModeSelected_WithNoManifestPath_ThrowsRatherThanSilentlyWastingWork(ChecksumMode mode)
    {
        // Regression test: Verify/Update with a blank manifest path used to silently fall
        // through to Generate, which itself only writes a manifest when the path is non-blank
        // -- so it hashed the whole folder and persisted nothing, with no indication anything
        // was skipped.
        var runner = new VerificationRunner(
            new FakeFileSystemEnumerator(), new FakeFileHasher(), new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>()));
        var settings = NewSettings();
        settings.ChecksumMode = mode;
        settings.ChecksumManifestPath = null;

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.RunAsync(_root, settings));
    }

    [Fact]
    public async Task RunAsync_NeitherChecksumNorFlacConfigured_ReturnsAllNulls()
    {
        var runner = new VerificationRunner(
            new FakeFileSystemEnumerator(), new FakeFileHasher(), new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>()));

        var result = await runner.RunAsync(_root, NewSettings());

        Assert.Null(result.ChecksumOutcomeSummary);
        Assert.Null(result.FlacOutcomeSummary);
    }

    [Fact]
    public async Task RunAsync_ReportsPhaseMessage_ImmediatelyBeforeAnyFileCompletes()
    {
        var manifestPath = Path.Combine(_root, "manifest.sha256");
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("a.txt", false));
        var hasher = new FakeFileHasher(new Dictionary<string, string> { [ChecksumFileDiscovery.ToAbsolutePath(_root, "a.txt")] = "digest" });
        var runner = new VerificationRunner(enumerator, hasher, new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>()));
        var settings = NewSettings();
        settings.ChecksumMode = ChecksumMode.Generate;
        settings.ChecksumManifestPath = manifestPath;
        var progress = new RecordingProgress<VerificationProgress>();

        await runner.RunAsync(_root, settings, verificationProgress: progress);

        Assert.Contains(progress.Reports, r => r is { PhaseMessage: "Generating checksums. This may take a while.", FilesCompleted: 0 });
        Assert.Contains(progress.Reports, r => r.FilesCompleted == 1);
    }
}
