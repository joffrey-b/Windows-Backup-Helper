using WindowsBackupHelper.Core.Checksums;
using WindowsBackupHelper.Core.Exclusions;
using WindowsBackupHelper.Core.Tests.Exclusions;

namespace WindowsBackupHelper.Core.Tests.Checksums;

public sealed class ChecksumGenerateServiceTests
{
    [Fact]
    public async Task GenerateAsync_HashesEveryDiscoveredFile()
    {
        const string root = "/root";
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("a.txt", false),
            new FileSystemEntry("sub/b.flac", false));
        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [ChecksumFileDiscovery.ToAbsolutePath(root, "a.txt")] = "digest-a",
            [ChecksumFileDiscovery.ToAbsolutePath(root, "sub/b.flac")] = "digest-b",
        });

        var result = await new ChecksumGenerateService(enumerator, hasher).GenerateAsync(root);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal("digest-a", result.Entries["a.txt"]);
        Assert.Equal("digest-b", result.Entries["sub/b.flac"]);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task GenerateAsync_ReportsProgress_OnceForEveryFile()
    {
        const string root = "/root";
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("a.txt", false),
            new FileSystemEntry("sub/b.flac", false));
        var hasher = new FakeFileHasher(new Dictionary<string, string>
        {
            [ChecksumFileDiscovery.ToAbsolutePath(root, "a.txt")] = "digest-a",
            [ChecksumFileDiscovery.ToAbsolutePath(root, "sub/b.flac")] = "digest-b",
        });
        var progress = new RecordingProgress<int>();

        await new ChecksumGenerateService(enumerator, hasher).GenerateAsync(root, progress: progress);

        Assert.Equal(2, progress.Reports.Count);
        Assert.Equal(2, progress.Reports.Max());
    }

    [Fact]
    public async Task GenerateAsync_UnreadableFile_IsBucketedAsAnErrorNotAThrow()
    {
        const string root = "/root";
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("broken.flac", false));
        var hasher = new FakeFileHasher(throwsForAbsolutePath: [ChecksumFileDiscovery.ToAbsolutePath(root, "broken.flac")]);

        var result = await new ChecksumGenerateService(enumerator, hasher).GenerateAsync(root);

        Assert.Empty(result.Entries);
        Assert.Single(result.Errors);
        Assert.Equal("broken.flac", result.Errors[0].RelativePath);
    }
}
