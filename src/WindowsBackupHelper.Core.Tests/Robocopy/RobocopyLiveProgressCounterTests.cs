using WindowsBackupHelper.Core.Robocopy;

namespace WindowsBackupHelper.Core.Tests.Robocopy;

public sealed class RobocopyLiveProgressCounterTests
{
    private const string Separator = "-------------------------------------------------------------------------------";

    private const string HeaderBlock =
        "\n" + Separator + "\n" +
        "   ROBOCOPY     ::     Robust File Copy for Windows\n" +
        Separator + "\n" +
        "\n" +
        "  Started : mercredi 15 juillet 2026 18:00:55\n" +
        "   Source : \\\\nas\\Backups\\\n" +
        "     Dest : D:\\Backup\\\n" +
        "\n" +
        "    Files : *.*\n" +
        "\n" +
        "  Options : *.* /S /E /DCOPY:DA /COPY:DAT /PURGE /MIR /R:3 /W:5\n" +
        "\n" + Separator + "\n" +
        "\n";

    // Modeled directly on a real /UNILOG file (with /NP applied, so no percent-copied spam
    // appended to each file line) — header/job-info block, then directory and file entries.
    // Directory entries: "\t<count>\t<UNC path ending in \>". File entries: "\t<tag>\t\t<size>\t<filename>".
    private const string RealisticPartialLog =
        HeaderBlock +
        "\t                   0\t\\\\nas\\Backups\\\n" +
        "\t                  31\t\\\\nas\\Backups\\backupcodes\\\n" +
        "\t    New File  \t\t  39.2 m\tmaloja_export_20260713_030001.json\n" +
        "\t    New File  \t\t  39.3 m\tmaloja_export_20260714_030001.json\n" +
        "\t                   1\t\\\\nas\\Backups\\bookmarks\\\n" +
        "\t    New File  \t\t    5892\tvzdump-qemu-100-2026_07_14-04_30_01.log\n";

    [Fact]
    public void CountCompletedFiles_CountsOnlyFileEntries_NotDirectoryEntries()
    {
        // 3 file entries ("New File" lines), 3 directory entries (end in "\") that must NOT
        // be counted.
        var count = RobocopyLiveProgressCounter.CountCompletedFiles(RealisticPartialLog);

        Assert.Equal(3, count);
    }

    [Fact]
    public void CountCompletedFiles_IgnoresHeaderLinesBeforeTheThirdSeparator()
    {
        // "Files : *.*" in the header block would otherwise look like a qualifying line
        // (doesn't end in "\") if the 3-separator gate weren't applied.
        var count = RobocopyLiveProgressCounter.CountCompletedFiles(HeaderBlock);

        Assert.Equal(0, count);
    }

    [Fact]
    public void CountCompletedFiles_GrowsAsMoreEntriesAreAppended_SimulatingATailedLiveFile()
    {
        var afterFirstFile = RobocopyLiveProgressCounter.CountCompletedFiles(
            HeaderBlock +
            "\t                   0\t\\\\nas\\Backups\\\n" +
            "\t                  31\t\\\\nas\\Backups\\backupcodes\\\n" +
            "\t    New File  \t\t  39.2 m\tmaloja_export_20260713_030001.json\n");

        Assert.Equal(1, afterFirstFile);

        var afterAllFiles = RobocopyLiveProgressCounter.CountCompletedFiles(RealisticPartialLog);
        Assert.Equal(3, afterAllFiles);
    }

    [Fact]
    public void CountCompletedFiles_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, RobocopyLiveProgressCounter.CountCompletedFiles(string.Empty));
    }
}
