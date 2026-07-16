using WindowsBackupHelper.Core.Exclusions;

namespace WindowsBackupHelper.Core.Tests.Exclusions;

public sealed class DirectoryFileSystemEnumeratorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"wbh-fsenum-{Guid.NewGuid():N}");

    public DirectoryFileSystemEnumeratorTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Artist", "Album"));
        File.WriteAllText(Path.Combine(_root, "Artist", "Album", "track.flac"), "data");
        File.WriteAllText(Path.Combine(_root, "root-file.txt"), "data");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Enumerate_ReturnsForwardSlashNormalizedRelativePaths_TaggedByType()
    {
        var entries = new DirectoryFileSystemEnumerator().Enumerate(_root).ToList();

        Assert.Contains(entries, e => e.RelativePath == "Artist" && e.IsDirectory);
        Assert.Contains(entries, e => e.RelativePath == "Artist/Album" && e.IsDirectory);
        Assert.Contains(entries, e => e.RelativePath == "Artist/Album/track.flac" && !e.IsDirectory);
        Assert.Contains(entries, e => e.RelativePath == "root-file.txt" && !e.IsDirectory);
        Assert.DoesNotContain(entries, e => e.RelativePath.Contains('\\'));
    }
}
