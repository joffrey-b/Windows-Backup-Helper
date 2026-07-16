using WindowsBackupHelper.Core.Checksums;

namespace WindowsBackupHelper.Core.Tests.Checksums;

public sealed class ChecksumReportWriterTests
{
    private static readonly DateTime FixedTimestamp = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GenerateMarkdownForVerify_NoProblems_ShowsTheAllClearSection()
    {
        var result = new ChecksumVerifyResult(["a.flac"], [], [], []);

        var markdown = ChecksumReportWriter.GenerateMarkdownForVerify(result, "/root", TimeSpan.FromSeconds(1.2), FixedTimestamp);

        Assert.Contains("## ✅ No Problems Found", markdown);
        Assert.DoesNotContain("Problems at a Glance", markdown);
    }

    [Fact]
    public void GenerateMarkdownForVerify_WithProblems_ListsChangedMissingAndReadErrors()
    {
        var result = new ChecksumVerifyResult(
            Ok: ["ok.flac"],
            Changed: ["changed.flac"],
            Missing: ["missing.flac"],
            ReadErrors: [("unreadable.flac", "Access denied")]);

        var markdown = ChecksumReportWriter.GenerateMarkdownForVerify(result, "/root", TimeSpan.FromSeconds(1), FixedTimestamp);

        Assert.Contains("## ⚡ Problems at a Glance", markdown);
        Assert.Contains("🔄 `changed.flac`", markdown);
        Assert.Contains("❓ `missing.flac`", markdown);
        Assert.Contains("⚠️ `unreadable.flac`", markdown);
        Assert.Contains("_Access denied_", markdown);
    }

    [Fact]
    public void GenerateMarkdownForVerify_SummaryTable_CountsEachStatus()
    {
        var result = new ChecksumVerifyResult(
            Ok: ["a.flac", "b.flac"],
            Changed: ["c.flac"],
            Missing: ["d.flac"],
            ReadErrors: [("e.flac", "boom")]);

        var markdown = ChecksumReportWriter.GenerateMarkdownForVerify(result, "/root", TimeSpan.Zero, FixedTimestamp);

        Assert.Contains("| ✅ OK | 2 |", markdown);
        Assert.Contains("| 🔄 Changed | 1 |", markdown);
        Assert.Contains("| ❓ Missing | 1 |", markdown);
        Assert.Contains("| ⚠️ Read error | 1 |", markdown);
        Assert.Contains("| **Total**   | **5** |", markdown);
    }

    [Fact]
    public void GenerateMarkdownForVerify_GroupsByFolder_UsingForwardSlashRelativePaths()
    {
        var result = new ChecksumVerifyResult(Ok: ["Artist/Album/track.flac", "root-level.flac"], Changed: [], Missing: [], ReadErrors: []);

        var markdown = ChecksumReportWriter.GenerateMarkdownForVerify(result, "/root", TimeSpan.Zero, FixedTimestamp);

        Assert.Contains("`Artist/Album`", markdown);
        Assert.Contains("`(root)`", markdown);
    }

    [Fact]
    public void GenerateMarkdownForVerify_EscapesPipeCharactersInDetailColumn()
    {
        var result = new ChecksumVerifyResult(Ok: [], Changed: [], Missing: [], ReadErrors: [("a.flac", "message | with a pipe")]);

        var markdown = ChecksumReportWriter.GenerateMarkdownForVerify(result, "/root", TimeSpan.Zero, FixedTimestamp);

        Assert.Contains(@"message \| with a pipe", markdown);
    }

    [Fact]
    public void GenerateMarkdownForVerify_UsesLfLineEndingsOnly()
    {
        var result = new ChecksumVerifyResult(Ok: ["a.flac"], Changed: [], Missing: [], ReadErrors: []);

        var markdown = ChecksumReportWriter.GenerateMarkdownForVerify(result, "/root", TimeSpan.Zero, FixedTimestamp);

        Assert.DoesNotContain("\r\n", markdown);
    }

    [Fact]
    public void GenerateMarkdownForGenerate_NoErrors_ShowsTheAllClearSection()
    {
        var result = new ChecksumGenerateResult(new Dictionary<string, string> { ["a.flac"] = "digest" }, []);

        var markdown = ChecksumReportWriter.GenerateMarkdownForGenerate(result, "/root", TimeSpan.FromSeconds(2), FixedTimestamp);

        Assert.Contains("## ✅ No Problems Found", markdown);
        Assert.Contains("| ✅ Hashed | 1 |", markdown);
    }

    [Fact]
    public void GenerateMarkdownForGenerate_WithErrors_ListsThemInProblemsAtAGlance()
    {
        var result = new ChecksumGenerateResult(
            new Dictionary<string, string> { ["ok.flac"] = "digest" },
            [("broken.flac", "Access denied")]);

        var markdown = ChecksumReportWriter.GenerateMarkdownForGenerate(result, "/root", TimeSpan.Zero, FixedTimestamp);

        Assert.Contains("## ⚡ Problems at a Glance", markdown);
        Assert.Contains("⚠️ `broken.flac`", markdown);
        Assert.Contains("_Access denied_", markdown);
        Assert.Contains("| **Total**   | **2** |", markdown);
    }

    [Fact]
    public void GenerateMarkdownForUpdate_NoChanges_ShowsTheAllClearSection()
    {
        var result = new ChecksumUpdateResult(new Dictionary<string, string> { ["a.flac"] = "digest" }, [], [], []);

        var markdown = ChecksumReportWriter.GenerateMarkdownForUpdate(result, "/root", TimeSpan.FromSeconds(0.5), FixedTimestamp);

        Assert.Contains("## ✅ No Problems Found", markdown);
    }

    [Fact]
    public void GenerateMarkdownForUpdate_AddedAndRemoved_ListsBoth()
    {
        var result = new ChecksumUpdateResult(
            new Dictionary<string, string> { ["new.flac"] = "digest" },
            Added: ["new.flac"],
            Removed: ["gone.flac"],
            Errors: []);

        var markdown = ChecksumReportWriter.GenerateMarkdownForUpdate(result, "/root", TimeSpan.Zero, FixedTimestamp);

        Assert.Contains("➕ `new.flac`", markdown);
        Assert.Contains("➖ `gone.flac`", markdown);
        Assert.Contains("| ➕ Added | 1 |", markdown);
        Assert.Contains("| ➖ Removed | 1 |", markdown);
    }

    [Fact]
    public void WriteToFile_WritesNoBom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wbh-checksum-report-{Guid.NewGuid():N}.md");
        try
        {
            ChecksumReportWriter.WriteToFile("# Report\n", path);
            var bytes = File.ReadAllBytes(path);

            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
