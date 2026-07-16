using WindowsBackupHelper.Core.Exclusions;
using WindowsBackupHelper.Core.Flac;
using WindowsBackupHelper.Core.Tests.Exclusions;

namespace WindowsBackupHelper.Core.Tests.Flac;

public sealed class FlacAuditServiceTests
{
    private const string Root = "/root";

    private static string AbsolutePath(string relativePath) => Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public async Task RunAsync_OnlyConsidersFlacFiles()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("track.flac", false),
            new FileSystemEntry("cover.jpg", false),
            new FileSystemEntry("SubDir", true));
        var runner = new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>());

        var results = await new FlacAuditService(enumerator, runner).RunAsync(Root, workers: 2);

        Assert.Single(results);
        Assert.Equal("track.flac", results[0].RelativePath);
    }

    [Fact]
    public async Task RunAsync_SortsErrorsFirst_ThenWarnings_ThenOk_ThenByPath()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("b-ok.flac", false),
            new FileSystemEntry("a-error.flac", false),
            new FileSystemEntry("c-warning.flac", false),
            new FileSystemEntry("a-ok.flac", false));

        var runner = new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>
        {
            [AbsolutePath("a-error.flac")] = new(1, "", "ERROR: bad"),
            [AbsolutePath("c-warning.flac")] = new(0, "", "WARNING: no md5"),
        });

        var results = await new FlacAuditService(enumerator, runner).RunAsync(Root, workers: 2);

        Assert.Equal(
            ["a-error.flac", "c-warning.flac", "a-ok.flac", "b-ok.flac"],
            results.Select(r => r.RelativePath));
    }

    [Fact]
    public async Task RunAsync_ReportsProgress_OnceForEveryFlacFile()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("a.flac", false),
            new FileSystemEntry("b.flac", false),
            new FileSystemEntry("cover.jpg", false));
        var runner = new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>());
        var progress = new RecordingProgress<int>();

        await new FlacAuditService(enumerator, runner).RunAsync(Root, workers: 2, progress: progress);

        Assert.Equal(2, progress.Reports.Count);
        Assert.Equal(2, progress.Reports.Max());
    }

    [Fact]
    public async Task RunAsync_ClassifiesEachFileIndependently()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("good.flac", false),
            new FileSystemEntry("bad.flac", false));
        var runner = new FakeFlacProcessRunner(new Dictionary<string, FlacProcessResult>
        {
            [AbsolutePath("bad.flac")] = new(1, "", "ERROR: corrupt"),
        });

        var results = await new FlacAuditService(enumerator, runner).RunAsync(Root, workers: 2);

        Assert.Equal(FlacFileStatus.Ok, results.Single(r => r.RelativePath == "good.flac").Status);
        Assert.Equal(FlacFileStatus.Error, results.Single(r => r.RelativePath == "bad.flac").Status);
    }
}
