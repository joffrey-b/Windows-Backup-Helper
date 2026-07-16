using WindowsBackupHelper.Core.Checksums;
using WindowsBackupHelper.Core.Exclusions;
using WindowsBackupHelper.Core.Tests.Exclusions;

namespace WindowsBackupHelper.Core.Tests.Checksums;

public sealed class ChecksumFileDiscoveryTests
{
    [Fact]
    public void FindAllFiles_SkipsJunkByExactName_CaseInsensitive()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("Thumbs.db", false),
            new FileSystemEntry("thumbs.DB", false),
            new FileSystemEntry(".DS_Store", false),
            new FileSystemEntry("desktop.ini", false),
            new FileSystemEntry("keep.txt", false));

        var files = ChecksumFileDiscovery.FindAllFiles(enumerator, "/root");

        Assert.Equal(["keep.txt"], files);
    }

    [Fact]
    public void FindAllFiles_SkipsJunkBySuffix_CaseInsensitive()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("a.tmp", false),
            new FileSystemEntry("b.TMP", false),
            new FileSystemEntry("c.part", false),
            new FileSystemEntry("d.crdownload", false),
            new FileSystemEntry("e.lnk", false),
            new FileSystemEntry("keep.flac", false));

        var files = ChecksumFileDiscovery.FindAllFiles(enumerator, "/root");

        Assert.Equal(["keep.flac"], files);
    }

    [Fact]
    public void FindAllFiles_ExcludesDirectories()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("SomeDir", true),
            new FileSystemEntry("SomeDir/file.txt", false));

        var files = ChecksumFileDiscovery.FindAllFiles(enumerator, "/root");

        Assert.Equal(["SomeDir/file.txt"], files);
    }

    [Fact]
    public void FindAllFiles_SortsByOrdinalPath()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("b.txt", false),
            new FileSystemEntry("Z.txt", false),
            new FileSystemEntry("a.txt", false));

        var files = ChecksumFileDiscovery.FindAllFiles(enumerator, "/root");

        Assert.Equal(["Z.txt", "a.txt", "b.txt"], files);
    }
}
