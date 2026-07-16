using System.Text;
using WindowsBackupHelper.Core.Checksums;

namespace WindowsBackupHelper.Core.Tests.Checksums;

public sealed class ChecksumManifestTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"wbh-manifest-{Guid.NewGuid():N}.sha256");

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }

    [Fact]
    public void Write_UsesTwoSpaceSeparator_LfEndings_NoBom_SortedByOrdinalPath()
    {
        ChecksumManifest.Write(
            new Dictionary<string, string>
            {
                ["b.txt"] = "bbbb",
                ["a.txt"] = "aaaa",
                ["Z.txt"] = "zzzz", // uppercase sorts before lowercase in ordinal order
            },
            _path);

        var bytes = File.ReadAllBytes(_path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "must not emit a UTF-8 BOM");
        Assert.DoesNotContain((byte)'\r', bytes);

        var text = Encoding.UTF8.GetString(bytes);
        // Ordinal order: uppercase 'Z' (0x5A) sorts before lowercase 'a' (0x61) and 'b' (0x62).
        Assert.Equal("zzzz  Z.txt\naaaa  a.txt\nbbbb  b.txt\n", text);
    }

    [Fact]
    public void ReadThenWrite_RoundTrips()
    {
        var original = new Dictionary<string, string> { ["dir/file.flac"] = new string('a', 64) };
        ChecksumManifest.Write(original, _path);

        var read = ChecksumManifest.Read(_path);

        Assert.Equal(original, read);
    }

    [Fact]
    public void Read_NormalizesBackslashesToForwardSlashes()
    {
        File.WriteAllText(_path, $"{new string('a', 64)}  dir\\sub\\file.flac\n", new UTF8Encoding(false));

        var entries = ChecksumManifest.Read(_path);

        Assert.True(entries.ContainsKey("dir/sub/file.flac"));
    }

    [Fact]
    public void Read_SkipsBlankLinesAndCommentLines()
    {
        File.WriteAllText(_path, $"# a comment\n\n{new string('a', 64)}  file.txt\n", new UTF8Encoding(false));

        var entries = ChecksumManifest.Read(_path);

        Assert.Single(entries);
        Assert.True(entries.ContainsKey("file.txt"));
    }

    [Fact]
    public void Read_SkipsMalformedLinesMissingTheTwoSpaceSeparator()
    {
        File.WriteAllText(_path, "not-a-valid-manifest-line\n", new UTF8Encoding(false));

        var entries = ChecksumManifest.Read(_path);

        Assert.Empty(entries);
    }
}
