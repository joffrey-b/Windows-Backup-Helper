using WindowsBackupHelper.Core.Flac;

namespace WindowsBackupHelper.Core.Tests.Flac;

public sealed class FlacAuditReportWriterTests
{
    private static readonly DateTime FixedTimestamp = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void GenerateMarkdown_NoProblems_ShowsTheAllClearSection()
    {
        var results = new[] { new FlacFileResult("a.flac", FlacFileStatus.Ok, []) };

        var markdown = FlacAuditReportWriter.GenerateMarkdown(results, "/root", TimeSpan.FromSeconds(1.2), errorsOnly: false, FixedTimestamp);

        Assert.Contains("## ✅ No Problems Found", markdown);
        Assert.DoesNotContain("Problems at a Glance", markdown);
    }

    [Fact]
    public void GenerateMarkdown_WithProblems_ListsThemInProblemsAtAGlance()
    {
        var results = new[]
        {
            new FlacFileResult("bad.flac", FlacFileStatus.Error, ["ERROR: corrupt stream"]),
            new FlacFileResult("ok.flac", FlacFileStatus.Ok, []),
        };

        var markdown = FlacAuditReportWriter.GenerateMarkdown(results, "/root", TimeSpan.FromSeconds(1), errorsOnly: false, FixedTimestamp);

        Assert.Contains("## ⚡ Problems at a Glance", markdown);
        Assert.Contains("❌ `bad.flac`", markdown);
        Assert.Contains("_ERROR: corrupt stream_", markdown);
    }

    [Fact]
    public void GenerateMarkdown_SummaryTable_CountsEachStatus()
    {
        var results = new[]
        {
            new FlacFileResult("a.flac", FlacFileStatus.Ok, []),
            new FlacFileResult("b.flac", FlacFileStatus.Warning, ["w"]),
            new FlacFileResult("c.flac", FlacFileStatus.Error, ["e"]),
        };

        var markdown = FlacAuditReportWriter.GenerateMarkdown(results, "/root", TimeSpan.Zero, errorsOnly: false, FixedTimestamp);

        Assert.Contains("| **Total**   | **3** |", markdown);
    }

    [Fact]
    public void GenerateMarkdown_ErrorsOnly_OmitsOkRowsFromFolderTables_ButKeepsThemInSummary()
    {
        var results = new[]
        {
            new FlacFileResult("Album/ok.flac", FlacFileStatus.Ok, []),
            new FlacFileResult("Album/bad.flac", FlacFileStatus.Error, ["e"]),
        };

        var markdown = FlacAuditReportWriter.GenerateMarkdown(results, "/root", TimeSpan.Zero, errorsOnly: true, FixedTimestamp);

        Assert.Contains("| **Total**   | **2** |", markdown); // summary still counts everything
        Assert.Contains("`bad.flac`", markdown);
        Assert.DoesNotContain("`ok.flac`", markdown);
    }

    [Fact]
    public void GenerateMarkdown_GroupsByFolder_UsingForwardSlashRelativePaths()
    {
        var results = new[]
        {
            new FlacFileResult("Artist/Album/track.flac", FlacFileStatus.Ok, []),
            new FlacFileResult("root-level.flac", FlacFileStatus.Ok, []),
        };

        var markdown = FlacAuditReportWriter.GenerateMarkdown(results, "/root", TimeSpan.Zero, errorsOnly: false, FixedTimestamp);

        Assert.Contains("`Artist/Album`", markdown);
        Assert.Contains("`(root)`", markdown);
    }

    [Fact]
    public void GenerateMarkdown_EscapesPipeCharactersInDetailColumn()
    {
        var results = new[] { new FlacFileResult("a.flac", FlacFileStatus.Error, ["message | with a pipe"]) };

        var markdown = FlacAuditReportWriter.GenerateMarkdown(results, "/root", TimeSpan.Zero, errorsOnly: false, FixedTimestamp);

        Assert.Contains(@"message \| with a pipe", markdown);
    }

    [Fact]
    public void GenerateMarkdown_UsesLfLineEndingsOnly()
    {
        var results = new[] { new FlacFileResult("a.flac", FlacFileStatus.Ok, []) };
        var markdown = FlacAuditReportWriter.GenerateMarkdown(results, "/root", TimeSpan.Zero, errorsOnly: false, FixedTimestamp);

        Assert.DoesNotContain("\r\n", markdown);
    }

    [Fact]
    public void WriteToFile_WritesNoBom()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wbh-flac-report-{Guid.NewGuid():N}.md");
        try
        {
            FlacAuditReportWriter.WriteToFile("# Report\n", path);
            var bytes = File.ReadAllBytes(path);

            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
