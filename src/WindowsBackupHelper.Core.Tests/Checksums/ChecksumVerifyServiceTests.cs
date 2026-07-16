using WindowsBackupHelper.Core.Checksums;

namespace WindowsBackupHelper.Core.Tests.Checksums;

public sealed class ChecksumVerifyServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"wbh-verify-{Guid.NewGuid():N}");

    public ChecksumVerifyServiceTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string CreateFile(string relativeName)
    {
        var path = Path.Combine(_root, relativeName);
        File.WriteAllText(path, "content");
        return path;
    }

    [Fact]
    public async Task VerifyAsync_MatchingDigest_IsOk()
    {
        var path = CreateFile("ok.txt");
        var hasher = new FakeFileHasher(new Dictionary<string, string> { [path] = "expected-digest" });
        var manifest = new Dictionary<string, string> { ["ok.txt"] = "expected-digest" };

        var result = await new ChecksumVerifyService(hasher).VerifyAsync(_root, manifest);

        Assert.Equal(["ok.txt"], result.Ok);
        Assert.Empty(result.Changed);
        Assert.Empty(result.Missing);
        Assert.Empty(result.ReadErrors);
    }

    [Fact]
    public async Task VerifyAsync_MismatchedDigest_IsChanged()
    {
        var path = CreateFile("changed.txt");
        var hasher = new FakeFileHasher(new Dictionary<string, string> { [path] = "actual-digest" });
        var manifest = new Dictionary<string, string> { ["changed.txt"] = "stale-digest" };

        var result = await new ChecksumVerifyService(hasher).VerifyAsync(_root, manifest);

        Assert.Equal(["changed.txt"], result.Changed);
    }

    [Fact]
    public async Task VerifyAsync_FileMissingFromDisk_IsNeverHashed_AndBucketedAsMissing()
    {
        // Deliberately no throwsFor list — if the service tried to hash a missing file it
        // would just get the harmless default digest, so this alone wouldn't catch a bug.
        // The real assertion is that it lands in Missing, not Ok/Changed.
        var hasher = new FakeFileHasher();
        var manifest = new Dictionary<string, string> { ["gone.txt"] = "whatever" };

        var result = await new ChecksumVerifyService(hasher).VerifyAsync(_root, manifest);

        Assert.Equal(["gone.txt"], result.Missing);
        Assert.Empty(result.Ok);
        Assert.Empty(result.Changed);
    }

    [Fact]
    public async Task VerifyAsync_ReadError_IsBucketedSeparately_FromChangedAndMissing()
    {
        var path = CreateFile("unreadable.txt");
        var hasher = new FakeFileHasher(throwsForAbsolutePath: [path]);
        var manifest = new Dictionary<string, string> { ["unreadable.txt"] = "expected" };

        var result = await new ChecksumVerifyService(hasher).VerifyAsync(_root, manifest);

        Assert.Single(result.ReadErrors);
        Assert.Equal("unreadable.txt", result.ReadErrors[0].RelativePath);
        Assert.Empty(result.Ok);
        Assert.Empty(result.Changed);
        Assert.Empty(result.Missing);
    }

    [Fact]
    public async Task VerifyAsync_ReportsProgress_OnlyForFilesActuallyHashed()
    {
        var path = CreateFile("ok.txt");
        var hasher = new FakeFileHasher(new Dictionary<string, string> { [path] = "expected-digest" });
        // "gone.txt" is missing from disk, so it's filtered out before hashing starts and must
        // not count toward progress -- only "ok.txt" is actually hashed.
        var manifest = new Dictionary<string, string> { ["ok.txt"] = "expected-digest", ["gone.txt"] = "whatever" };
        var progress = new RecordingProgress<int>();

        await new ChecksumVerifyService(hasher).VerifyAsync(_root, manifest, progress: progress);

        Assert.Equal([1], progress.Reports);
    }

    [Fact]
    public async Task VerifyAsync_FilesOnDiskButAbsentFromManifest_AreNotReportedAtAll()
    {
        CreateFile("known.txt");
        CreateFile("unlisted.txt"); // present on disk, not in the manifest
        var hasher = new FakeFileHasher(new Dictionary<string, string> { [Path.Combine(_root, "known.txt")] = "d" });
        var manifest = new Dictionary<string, string> { ["known.txt"] = "d" };

        var result = await new ChecksumVerifyService(hasher).VerifyAsync(_root, manifest);

        Assert.Equal(["known.txt"], result.Ok);
    }
}
