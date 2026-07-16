using WindowsBackupHelper.Core.Robocopy;
using WindowsBackupHelper.Win.Robocopy;

namespace WindowsBackupHelper.Win.Tests.Robocopy;

/// <summary>
/// Exercises a real robocopy.exe (built into Windows) copying between two local temp
/// folders, end to end through RobocopyArgumentBuilder -> RobocopyProcessRunner ->
/// RobocopyOutputParser -> RobocopyExitCodeInterpreter. This is the "spawn one real
/// Robocopy run end-to-end" exit criterion from docs/WINDOWS_HANDOFF.md's Phase 6, using
/// local folders since a real NAS isn't reachable from an automated test.
/// </summary>
public sealed class RobocopyProcessRunnerTests : IDisposable
{
    private readonly string _sourceDir = Path.Combine(Path.GetTempPath(), $"wbh-robocopy-src-{Guid.NewGuid():N}");
    private readonly string _destDir = Path.Combine(Path.GetTempPath(), $"wbh-robocopy-dst-{Guid.NewGuid():N}");

    public RobocopyProcessRunnerTests()
    {
        Directory.CreateDirectory(_sourceDir);
        File.WriteAllText(Path.Combine(_sourceDir, "track.txt"), "hello from the golden test");
        Directory.CreateDirectory(Path.Combine(_sourceDir, "sub"));
        File.WriteAllText(Path.Combine(_sourceDir, "sub", "nested.txt"), "nested contents");
    }

    public void Dispose()
    {
        if (Directory.Exists(_sourceDir)) Directory.Delete(_sourceDir, recursive: true);
        if (Directory.Exists(_destDir)) Directory.Delete(_destDir, recursive: true);
    }

    [Fact]
    public async Task RunAsync_RealRobocopy_MirrorsFilesAndReportsSuccessExitCode()
    {
        var options = new ResolvedRobocopyOptions { Mirror = true, CopySubdirectories = true, Retries = 1, WaitSeconds = 1 };
        var logPath = Path.Combine(Path.GetTempPath(), $"wbh-robocopy-log-{Guid.NewGuid():N}.log");
        var commandLine = RobocopyArgumentBuilder.Build(options, _sourceDir, _destDir, [], [], logPath);

        var runner = new RobocopyProcessRunner();
        var result = await runner.RunAsync(commandLine.Arguments);

        var outcome = RobocopyExitCodeInterpreter.Interpret(result.ExitCode);
        Assert.True(outcome.IsSuccess, $"Robocopy reported failure (exit {result.ExitCode}): {outcome.HumanReadableSummary}");

        Assert.True(File.Exists(Path.Combine(_destDir, "track.txt")));
        Assert.True(File.Exists(Path.Combine(_destDir, "sub", "nested.txt")));
        Assert.Equal("hello from the golden test", await File.ReadAllTextAsync(Path.Combine(_destDir, "track.txt")));

        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }
    }

    [Fact]
    public async Task RunAsync_DryRun_ReportsWhatWouldHappen_ButCopiesNothing()
    {
        var options = new ResolvedRobocopyOptions { Mirror = true, CopySubdirectories = true, Retries = 1, WaitSeconds = 1 };
        var logPath = Path.Combine(Path.GetTempPath(), $"wbh-robocopy-log-{Guid.NewGuid():N}.log");
        var commandLine = RobocopyArgumentBuilder.Build(options, _sourceDir, _destDir, [], [], logPath, dryRun: true);

        var result = await new RobocopyProcessRunner().RunAsync(commandLine.Arguments);

        var outcome = RobocopyExitCodeInterpreter.Interpret(result.ExitCode);
        Assert.True(outcome.IsSuccess);
        Assert.False(File.Exists(Path.Combine(_destDir, "track.txt")), "a dry run (/L) must not copy anything");

        if (File.Exists(logPath))
        {
            File.Delete(logPath);
        }
    }

    [Fact]
    public async Task RunAsync_NonexistentRobocopyExecutable_ThrowsFileNotFoundException()
    {
        var runner = new RobocopyProcessRunner(@"C:\does\not\exist\robocopy.exe");

        await Assert.ThrowsAsync<FileNotFoundException>(() => runner.RunAsync([_sourceDir, _destDir]));
    }
}
