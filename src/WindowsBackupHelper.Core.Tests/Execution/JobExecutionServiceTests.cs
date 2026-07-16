using System.Text;
using WindowsBackupHelper.Core.Checksums;
using WindowsBackupHelper.Core.Exclusions;
using WindowsBackupHelper.Core.Execution;
using WindowsBackupHelper.Core.Flac;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Robocopy;
using WindowsBackupHelper.Core.Tests.Checksums;
using WindowsBackupHelper.Core.Tests.Data;
using WindowsBackupHelper.Core.Tests.Exclusions;
using WindowsBackupHelper.Core.Tests.Flac;

namespace WindowsBackupHelper.Core.Tests.Execution;

public sealed class JobExecutionServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly string _logDirectory = Path.Combine(Path.GetTempPath(), $"wbh-exec-logs-{Guid.NewGuid():N}");

    private readonly RobocopyOptionSetRepository _optionSetRepository;
    private readonly ExclusionRuleRepository _exclusionRuleRepository;
    private readonly CredentialTargetRepository _credentialTargetRepository;
    private readonly VerificationSettingsRepository _verificationSettingsRepository;
    private readonly AppSettingsRepository _appSettingsRepository;
    private readonly RunHistoryRepository _runHistoryRepository;
    private readonly FolderPairRunResultRepository _runResultRepository;

    private readonly FakeCredentialStore _credentialStore = new();
    private readonly FakeSmbConnectionManager _smbConnectionManager = new();
    private readonly FakeNotificationService _notificationService = new();

    public JobExecutionServiceTests()
    {
        _optionSetRepository = new RobocopyOptionSetRepository(_db.Connection);
        _exclusionRuleRepository = new ExclusionRuleRepository(_db.Connection);
        _credentialTargetRepository = new CredentialTargetRepository(_db.Connection);
        _verificationSettingsRepository = new VerificationSettingsRepository(_db.Connection);
        _appSettingsRepository = new AppSettingsRepository(_db.Connection);
        _runHistoryRepository = new RunHistoryRepository(_db.Connection);
        _runResultRepository = new FolderPairRunResultRepository(_db.Connection);

        var appOptionSetId = Guid.NewGuid().ToString();
        _optionSetRepository.InsertAsync(new RobocopyOptionSet { Id = appOptionSetId, Retries = 3, WaitSeconds = 5 }).GetAwaiter().GetResult();
        _appSettingsRepository.InsertAsync(new AppSettings { DefaultRobocopyOptionSetId = appOptionSetId }).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_logDirectory))
        {
            Directory.Delete(_logDirectory, recursive: true);
        }
    }

    private JobExecutionService CreateService(
        FakeRobocopyProcessRunner robocopyRunner,
        IFileSystemEnumerator? fileSystemEnumerator = null,
        IFileHasher? fileHasher = null,
        IFlacProcessRunner? flacProcessRunner = null)
    {
        var enumerator = fileSystemEnumerator ?? new FakeFileSystemEnumerator();
        var verificationRunner = new VerificationRunner(
            enumerator,
            fileHasher ?? new FakeFileHasher(),
            flacProcessRunner ?? new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>()));

        return new(
            _optionSetRepository,
            _exclusionRuleRepository,
            _credentialTargetRepository,
            _verificationSettingsRepository,
            _appSettingsRepository,
            _runHistoryRepository,
            _runResultRepository,
            _credentialStore,
            _smbConnectionManager,
            robocopyRunner,
            enumerator,
            verificationRunner,
            _notificationService,
            _logDirectory);
    }

    private async Task<Job> CreateJobAsync()
    {
        var job = new Job { Id = Guid.NewGuid().ToString(), Name = $"Job-{Guid.NewGuid():N}", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow };
        await new JobRepository(_db.Connection).InsertAsync(job);
        return job;
    }

    private async Task<FolderPair> CreateFolderPairAsync(string jobId, string source, string destination)
    {
        var pair = new FolderPair
        {
            Id = Guid.NewGuid().ToString(),
            JobId = jobId,
            SourcePath = source,
            DestinationPath = destination,
        };
        await new FolderPairRepository(_db.Connection).InsertAsync(pair);
        return pair;
    }

    [Fact]
    public async Task RunJobAsync_SuccessfulRobocopy_PersistsRunHistoryAndResult_AndNotifiesSuccess()
    {
        var job = await CreateJobAsync();
        var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", @"C:\Dest");
        var runner = new FakeRobocopyProcessRunner(_ => new RobocopyProcessResult(1, "Files : 1 1 0 0 0 0"));
        var service = CreateService(runner);

        var runHistory = await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

        Assert.Equal(RunOutcome.Success, runHistory.OverallOutcome);
        Assert.NotNull(runHistory.CompletedUtc);
        Assert.Single(_notificationService.Notifications);
        Assert.True(_notificationService.Notifications[0].Success);

        var persistedResults = await _runResultRepository.GetByRunHistoryIdAsync(runHistory.Id);
        Assert.Single(persistedResults);
        Assert.Equal(1, persistedResults[0].RobocopyExitCode);
        Assert.Equal(1, persistedResults[0].FilesCopied);
    }

    [Fact]
    public async Task RunJobAsync_ParsesSummaryFromTheUnilogFile_NotFromEmptyConsoleOutput()
    {
        // Real Robocopy with /UNILOG(+) writes its entire summary to the log file and leaves
        // console stdout empty — this reproduces that (rather than a fake that puts the
        // summary directly in StandardOutput, which no real invocation ever does).
        var job = await CreateJobAsync();
        var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", @"C:\Dest");
        var runner = new FakeRobocopyProcessRunner(args =>
        {
            var uniLogArg = args.Single(a => a.StartsWith("/UNILOG:", StringComparison.Ordinal));
            var logPath = uniLogArg["/UNILOG:".Length..];
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.WriteAllText(logPath, "   Files :         2         2         0         0         0         0\n", Encoding.Unicode);
            return new RobocopyProcessResult(1, "");
        });
        var service = CreateService(runner);

        var runHistory = await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

        var persistedResults = await _runResultRepository.GetByRunHistoryIdAsync(runHistory.Id);
        Assert.Equal(2, persistedResults[0].FilesCopied);
    }

    [Fact]
    public async Task RunJobAsync_ParsesSummary_WhenUnilogFileHasNoBomAndIsSingleByteEncoded()
    {
        // Regression test for a real-world bug: /UNILOG+ (append), used whenever "Append to
        // log" is checked, writing to a log path that doesn't exist yet (every run does, since
        // each gets its own unique log filename) has been observed to fall back to the
        // system's single-byte ANSI/OEM encoding with no BOM at all, instead of UTF-16LE.
        // Hardcoding Encoding.Unicode on read silently corrupted every character in that case,
        // leaving Files/Bytes copied blank even though the run genuinely copied files.
        var job = await CreateJobAsync();
        var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", @"C:\Dest");
        var runner = new FakeRobocopyProcessRunner(args =>
        {
            var uniLogArg = args.Single(a => a.StartsWith("/UNILOG:", StringComparison.Ordinal));
            var logPath = uniLogArg["/UNILOG:".Length..];
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            // Plain UTF-8/ASCII bytes, deliberately with no BOM — matches the raw bytes
            // observed from a real /UNILOG+-to-new-file run.
            File.WriteAllText(logPath, "   Files :         2         2         0         0         0         0\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return new RobocopyProcessResult(1, "");
        });
        var service = CreateService(runner);

        var runHistory = await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

        var persistedResults = await _runResultRepository.GetByRunHistoryIdAsync(runHistory.Id);
        Assert.Equal(2, persistedResults[0].FilesCopied);
    }

    [Fact]
    public async Task RunJobAsync_NeverInvokesRobocopyWithoutResolvedRetryAndWait()
    {
        var job = await CreateJobAsync();
        var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", @"C:\Dest");
        var runner = new FakeRobocopyProcessRunner();
        var service = CreateService(runner);

        await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

        var args = runner.Invocations.Single();
        Assert.Contains("/R:3", args);
        Assert.Contains("/W:5", args);
    }

    [Fact]
    public async Task RunJobAsync_DryRun_PassesSlashLFlag()
    {
        var job = await CreateJobAsync();
        var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", @"C:\Dest");
        var runner = new FakeRobocopyProcessRunner();
        var service = CreateService(runner);

        var runHistory = await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: true);

        Assert.Contains("/L", runner.Invocations.Single());
        Assert.True(runHistory.WasDryRun);
    }

    [Fact]
    public async Task RunJobAsync_FailedRobocopy_MarksOutcomeFailed_AndNotifiesFailure()
    {
        var job = await CreateJobAsync();
        var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", @"C:\Dest");
        var runner = new FakeRobocopyProcessRunner(_ => new RobocopyProcessResult(8, ""));
        var service = CreateService(runner);

        var runHistory = await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

        Assert.Equal(RunOutcome.Failed, runHistory.OverallOutcome);
        Assert.False(_notificationService.Notifications[0].Success);
    }

    [Fact]
    public async Task RunJobAsync_MixedPairResults_IsPartialFailure()
    {
        var job = await CreateJobAsync();
        var goodPair = await CreateFolderPairAsync(job.Id, @"C:\Source1", @"C:\Dest1");
        var badPair = await CreateFolderPairAsync(job.Id, @"C:\Source2", @"C:\Dest2");
        var runner = new FakeRobocopyProcessRunner(args => args[0] == @"C:\Source2"
            ? new RobocopyProcessResult(16, "")
            : new RobocopyProcessResult(1, ""));
        var service = CreateService(runner);

        var runHistory = await service.RunJobAsync(job, [goodPair, badPair], RunTriggerType.Manual, dryRun: false);

        Assert.Equal(RunOutcome.PartialFailure, runHistory.OverallOutcome);
        var persisted = await _runResultRepository.GetByRunHistoryIdAsync(runHistory.Id);
        Assert.Equal(2, persisted.Count);
    }

    [Fact]
    public async Task RunJobAsync_DisabledFolderPair_IsSkippedEntirely()
    {
        var job = await CreateJobAsync();
        var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", @"C:\Dest");
        pair.IsEnabled = false;
        var runner = new FakeRobocopyProcessRunner();
        var service = CreateService(runner);

        var runHistory = await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

        Assert.Empty(runner.Invocations);
        Assert.Empty(await _runResultRepository.GetByRunHistoryIdAsync(runHistory.Id));
    }

    [Fact]
    public async Task RunJobAsync_UncPathWithCredential_ConnectsAndDisconnectsUsingStoredCredential()
    {
        var job = await CreateJobAsync();
        var credentialTarget = new CredentialTarget
        {
            Id = Guid.NewGuid().ToString(), Label = "NAS", HostOrUncRoot = @"\\nas",
            CredentialManagerTargetName = "WindowsBackupHelper:nas-test",
        };
        await _credentialTargetRepository.InsertAsync(credentialTarget);
        _credentialStore.Seed(credentialTarget.CredentialManagerTargetName, "nas-user", "nas-password");

        var pair = await CreateFolderPairAsync(job.Id, @"\\nas\Source", @"C:\Dest");
        pair.SourceCredentialTargetId = credentialTarget.Id;
        var runner = new FakeRobocopyProcessRunner();
        var service = CreateService(runner);

        await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

        var connection = Assert.Single(_smbConnectionManager.Connections);
        Assert.Equal(@"\\nas\Source", connection.UncPath);
        Assert.Equal("nas-user", connection.UserName);
        Assert.Equal("nas-password", connection.Password);
        Assert.Contains(@"\\nas\Source", _smbConnectionManager.DisposedPaths);
    }

    [Fact]
    public async Task RunJobAsync_LocalPaths_NeverTouchesSmbConnectionManager()
    {
        var job = await CreateJobAsync();
        var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", @"D:\Dest");
        var service = CreateService(new FakeRobocopyProcessRunner());

        await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

        Assert.Empty(_smbConnectionManager.Connections);
    }

    [Fact]
    public async Task RunJobAsync_CredentialTargetWithNoStoredSecret_RecordsErrorForThatPair_WithoutFailingTheWholeJob()
    {
        var job = await CreateJobAsync();
        var credentialTarget = new CredentialTarget
        {
            Id = Guid.NewGuid().ToString(), Label = "NAS", HostOrUncRoot = @"\\nas",
            CredentialManagerTargetName = "WindowsBackupHelper:missing",
        };
        await _credentialTargetRepository.InsertAsync(credentialTarget);
        // Deliberately not seeding the credential store — simulates a dangling reference.

        var badPair = await CreateFolderPairAsync(job.Id, @"\\nas\Source", @"C:\Dest");
        badPair.SourceCredentialTargetId = credentialTarget.Id;
        var goodPair = await CreateFolderPairAsync(job.Id, @"C:\Source2", @"C:\Dest2");
        var service = CreateService(new FakeRobocopyProcessRunner());

        var runHistory = await service.RunJobAsync(job, [badPair, goodPair], RunTriggerType.Manual, dryRun: false);

        var results = await _runResultRepository.GetByRunHistoryIdAsync(runHistory.Id);
        var badResult = results.Single(r => r.FolderPairId == badPair.Id);
        Assert.NotNull(badResult.ErrorMessage);
        Assert.Contains("credential", badResult.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RunOutcome.PartialFailure, runHistory.OverallOutcome);
    }

    [Fact]
    public async Task RunFolderPairAsync_CollapsibleExclusionRule_IsPassedThroughToRobocopyArgs()
    {
        var job = await CreateJobAsync();
        var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", @"C:\Dest");
        await _exclusionRuleRepository.InsertAsync(new ExclusionRule
        {
            Scope = ExclusionScope.Global, PatternType = ExclusionPatternType.Wildcard,
            Pattern = "*.tmp", TargetType = ExclusionTargetType.File,
        });
        var runner = new FakeRobocopyProcessRunner();
        var service = CreateService(runner);

        await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

        var args = runner.Invocations.Single();
        Assert.Contains("/XF", args);
        Assert.Contains("*.tmp", args);
    }

    [Fact]
    public async Task RunFolderPairAsync_VerificationSettingsGenerate_WritesManifestAndSummary()
    {
        var job = await CreateJobAsync();
        var destDir = Path.Combine(Path.GetTempPath(), $"wbh-exec-dest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destDir);
        var manifestPath = Path.Combine(Path.GetTempPath(), $"wbh-exec-manifest-{Guid.NewGuid():N}.sha256");

        try
        {
            var verificationSettings = new VerificationSettings
            {
                Id = Guid.NewGuid().ToString(), ChecksumMode = ChecksumMode.Generate, ChecksumManifestPath = manifestPath,
            };
            await _verificationSettingsRepository.InsertAsync(verificationSettings);

            var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", destDir);
            pair.VerificationSettingsId = verificationSettings.Id;

            var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("a.txt", false));
            var hasher = new FakeFileHasher(new Dictionary<string, string> { [ChecksumFileDiscovery.ToAbsolutePath(destDir, "a.txt")] = "digest" });
            var service = CreateService(new FakeRobocopyProcessRunner(), fileSystemEnumerator: enumerator, fileHasher: hasher);

            var runHistory = await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

            var results = await _runResultRepository.GetByRunHistoryIdAsync(runHistory.Id);
            Assert.Contains("Generated 1 checksum", results[0].ChecksumOutcomeSummary);
            Assert.True(File.Exists(manifestPath));
        }
        finally
        {
            Directory.Delete(destDir, recursive: true);
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
        }
    }

    [Fact]
    public async Task RunJobAsync_VerificationFindsMissingFile_MarksOverallOutcomeAsSuccessWithMismatches()
    {
        // Regression test: AggregateOutcome used to decide pass/fail via a substring check
        // against ChecksumOutcomeSummary that a Missing-only result never tripped, so a run
        // with a clean robocopy exit code but genuinely missing destination files used to be
        // reported as plain Success instead of SuccessWithMismatches.
        var job = await CreateJobAsync();
        var destDir = Path.Combine(Path.GetTempPath(), $"wbh-exec-dest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(destDir);
        var manifestPath = Path.Combine(Path.GetTempPath(), $"wbh-exec-manifest-{Guid.NewGuid():N}.sha256");

        try
        {
            ChecksumManifest.Write(new Dictionary<string, string> { ["gone.txt"] = "digest" }, manifestPath);
            var verificationSettings = new VerificationSettings
            {
                Id = Guid.NewGuid().ToString(), ChecksumMode = ChecksumMode.VerifyAgainstManifest, ChecksumManifestPath = manifestPath,
            };
            await _verificationSettingsRepository.InsertAsync(verificationSettings);

            var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", destDir);
            pair.VerificationSettingsId = verificationSettings.Id;

            var runner = new FakeRobocopyProcessRunner(_ => new RobocopyProcessResult(0, "Files : 0 0 0 0 0 0"));
            var service = CreateService(runner);

            var runHistory = await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: false);

            var results = await _runResultRepository.GetByRunHistoryIdAsync(runHistory.Id);
            Assert.True(results[0].ChecksumHasIssues);
            Assert.Equal(RunOutcome.SuccessWithMismatches, runHistory.OverallOutcome);
        }
        finally
        {
            Directory.Delete(destDir, recursive: true);
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
        }
    }

    [Fact]
    public async Task RunFolderPairAsync_DryRun_SkipsVerificationEntirely_EvenWhenConfigured()
    {
        var job = await CreateJobAsync();
        var verificationSettings = new VerificationSettings { Id = Guid.NewGuid().ToString(), ChecksumMode = ChecksumMode.Generate, ChecksumManifestPath = @"C:\should-not-be-written.sha256" };
        await _verificationSettingsRepository.InsertAsync(verificationSettings);

        var pair = await CreateFolderPairAsync(job.Id, @"C:\Source", @"C:\Dest");
        pair.VerificationSettingsId = verificationSettings.Id;
        var service = CreateService(new FakeRobocopyProcessRunner());

        var runHistory = await service.RunJobAsync(job, [pair], RunTriggerType.Manual, dryRun: true);

        var results = await _runResultRepository.GetByRunHistoryIdAsync(runHistory.Id);
        Assert.Null(results[0].ChecksumOutcomeSummary);
    }
}
