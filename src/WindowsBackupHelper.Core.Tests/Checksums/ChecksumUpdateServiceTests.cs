using WindowsBackupHelper.Core.Checksums;
using WindowsBackupHelper.Core.Exclusions;
using WindowsBackupHelper.Core.Tests.Exclusions;

namespace WindowsBackupHelper.Core.Tests.Checksums;

public sealed class ChecksumUpdateServiceTests
{
    private const string Root = "/root";

    [Fact]
    public async Task UpdateAsync_HashesOnlyNewFiles_NeverRehashesKnownOnes()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("known.txt", false),
            new FileSystemEntry("new.txt", false));
        // "known.txt" is set to throw if hashed — proving the incremental update never
        // re-hashes a file already present in the manifest.
        var hasher = new FakeFileHasher(
            digestsByAbsolutePath: new Dictionary<string, string> { [ChecksumFileDiscovery.ToAbsolutePath(Root, "new.txt")] = "new-digest" },
            throwsForAbsolutePath: [ChecksumFileDiscovery.ToAbsolutePath(Root, "known.txt")]);
        var manifest = new Dictionary<string, string> { ["known.txt"] = "old-digest" };

        var result = await new ChecksumUpdateService(enumerator, hasher).UpdateAsync(Root, manifest);

        Assert.Equal(["new.txt"], result.Added);
        Assert.Empty(result.Errors);
        Assert.Equal("old-digest", result.UpdatedEntries["known.txt"]);
        Assert.Equal("new-digest", result.UpdatedEntries["new.txt"]);
    }

    [Fact]
    public async Task UpdateAsync_DropsDeletedEntries()
    {
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("still-here.txt", false));
        var hasher = new FakeFileHasher();
        var manifest = new Dictionary<string, string>
        {
            ["still-here.txt"] = "digest",
            ["deleted.txt"] = "digest",
        };

        var result = await new ChecksumUpdateService(enumerator, hasher).UpdateAsync(Root, manifest);

        Assert.Equal(["deleted.txt"], result.Removed);
        Assert.False(result.UpdatedEntries.ContainsKey("deleted.txt"));
        Assert.True(result.UpdatedEntries.ContainsKey("still-here.txt"));
    }

    [Fact]
    public async Task UpdateAsync_ReportsProgress_OnlyForNewlyHashedFiles()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("known.txt", false),
            new FileSystemEntry("new.txt", false));
        // "known.txt" would throw if hashed -- it must never be, since it's already in the
        // manifest, so progress should only ever report for "new.txt".
        var hasher = new FakeFileHasher(
            digestsByAbsolutePath: new Dictionary<string, string> { [ChecksumFileDiscovery.ToAbsolutePath(Root, "new.txt")] = "new-digest" },
            throwsForAbsolutePath: [ChecksumFileDiscovery.ToAbsolutePath(Root, "known.txt")]);
        var manifest = new Dictionary<string, string> { ["known.txt"] = "old-digest" };
        var progress = new RecordingProgress<int>();

        await new ChecksumUpdateService(enumerator, hasher).UpdateAsync(Root, manifest, progress: progress);

        Assert.Equal([1], progress.Reports);
    }

    [Fact]
    public async Task UpdateAsync_NoChanges_ReturnsManifestUnchanged()
    {
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("same.txt", false));
        var hasher = new FakeFileHasher(throwsForAbsolutePath: [ChecksumFileDiscovery.ToAbsolutePath(Root, "same.txt")]);
        var manifest = new Dictionary<string, string> { ["same.txt"] = "digest" };

        var result = await new ChecksumUpdateService(enumerator, hasher).UpdateAsync(Root, manifest);

        Assert.Empty(result.Added);
        Assert.Empty(result.Removed);
        Assert.Equal(manifest, result.UpdatedEntries);
    }
}
