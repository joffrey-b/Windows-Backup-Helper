using WindowsBackupHelper.Core.Robocopy;

namespace WindowsBackupHelper.Core.Tests.Robocopy;

public sealed class RobocopyArgumentBuilderTests
{
    private static ResolvedRobocopyOptions Minimal(int retries = 3, int waitSeconds = 5) => new()
    {
        Retries = retries,
        WaitSeconds = waitSeconds,
    };

    [Fact]
    public void Build_SourceAndDestination_AreFirstTwoTokens()
    {
        var result = RobocopyArgumentBuilder.Build(
            Minimal(), @"\\nas\Music", @"D:\Backup\Music", [], [], @"C:\logs\run.log");

        Assert.Equal(@"\\nas\Music", result.Arguments[0]);
        Assert.Equal(@"D:\Backup\Music", result.Arguments[1]);
    }

    [Fact]
    public void Build_AlwaysEmitsRetryAndWaitFlags_EvenWhenNotExplicitlySet()
    {
        var result = RobocopyArgumentBuilder.Build(
            Minimal(retries: 3, waitSeconds: 5), "S", "D", [], [], @"C:\log.log");

        Assert.Contains("/R:3", result.Arguments);
        Assert.Contains("/W:5", result.Arguments);
    }

    [Fact]
    public void Build_AlwaysEmitsNoProgressFlag()
    {
        // /NP keeps Robocopy's per-file percent-copied spam out of the /UNILOG file — without
        // it, a single large file's progress updates get appended onto one enormous log line
        // (each update is \r-terminated, not \n-terminated, so it overwrites in place on a real
        // console but just keeps growing the same line in a log file) instead of \n-terminated
        // updates, making the log unreadable and per-file counting for live progress unreliable.
        var result = RobocopyArgumentBuilder.Build(Minimal(), "S", "D", [], [], "log.log");

        Assert.Contains("/NP", result.Arguments);
    }

    [Fact]
    public void Build_MirrorFlag_OnlyEmittedWhenTrue()
    {
        var withMirror = RobocopyArgumentBuilder.Build(Minimal() with { Mirror = true }, "S", "D", [], [], "log.log");
        var withoutMirror = RobocopyArgumentBuilder.Build(Minimal(), "S", "D", [], [], "log.log");

        Assert.Contains("/MIR", withMirror.Arguments);
        Assert.DoesNotContain("/MIR", withoutMirror.Arguments);
    }

    [Fact]
    public void Build_ValueFlags_IncludeTheirValueInline()
    {
        var options = Minimal() with { MultithreadCount = 8, CopyFlags = "DAT", MinFileSizeBytes = 1024 };
        var result = RobocopyArgumentBuilder.Build(options, "S", "D", [], [], "log.log");

        Assert.Contains("/MT:8", result.Arguments);
        Assert.Contains("/COPY:DAT", result.Arguments);
        Assert.Contains("/MIN:1024", result.Arguments);
    }

    [Fact]
    public void Build_ExcludeFilesAndDirs_EmittedAsXfAndXdWithTokensFollowing()
    {
        var result = RobocopyArgumentBuilder.Build(
            Minimal(), "S", "D", ["*.tmp", "Thumbs.db"], ["@eaDir", ".DS_Store"], "log.log");

        var args = result.Arguments;
        var xfIndex = args.ToList().IndexOf("/XF");
        var xdIndex = args.ToList().IndexOf("/XD");

        Assert.True(xfIndex >= 0);
        Assert.Equal("*.tmp", args[xfIndex + 1]);
        Assert.Equal("Thumbs.db", args[xfIndex + 2]);
        Assert.True(xdIndex >= 0);
        Assert.Equal("@eaDir", args[xdIndex + 1]);
        Assert.Equal(".DS_Store", args[xdIndex + 2]);
    }

    [Fact]
    public void Build_ExcludeLists_OmittedEntirelyWhenEmpty()
    {
        var result = RobocopyArgumentBuilder.Build(Minimal(), "S", "D", [], [], "log.log");

        Assert.DoesNotContain("/XF", result.Arguments);
        Assert.DoesNotContain("/XD", result.Arguments);
    }

    [Fact]
    public void Build_DryRun_AppendsSlashLFlag()
    {
        var result = RobocopyArgumentBuilder.Build(Minimal(), "S", "D", [], [], "log.log", dryRun: true);
        Assert.Contains("/L", result.Arguments);

        var realRun = RobocopyArgumentBuilder.Build(Minimal(), "S", "D", [], [], "log.log", dryRun: false);
        Assert.DoesNotContain("/L", realRun.Arguments);
    }

    [Fact]
    public void Build_AlwaysAppendsManagedUnilog_UsingPlusVariantWhenAppendToLogIsSet()
    {
        var fresh = RobocopyArgumentBuilder.Build(Minimal(), "S", "D", [], [], @"C:\logs\run.log");
        var appended = RobocopyArgumentBuilder.Build(Minimal() with { AppendToLog = true }, "S", "D", [], [], @"C:\logs\run.log");

        Assert.Contains(@"/UNILOG:C:\logs\run.log", fresh.Arguments);
        Assert.Contains(@"/UNILOG+:C:\logs\run.log", appended.Arguments);
    }

    [Fact]
    public void Build_ExtraRawArguments_TokenizedAndAppended_RespectingQuotedGroups()
    {
        var options = Minimal() with { ExtraRawArguments = "/NFL /XD \"My Folder\" /NDL" };
        var result = RobocopyArgumentBuilder.Build(options, "S", "D", [], [], "log.log");

        Assert.Contains("/NFL", result.Arguments);
        Assert.Contains("/XD", result.Arguments);
        Assert.Contains("My Folder", result.Arguments); // quoted group stays one token
        Assert.Contains("/NDL", result.Arguments);
        Assert.Empty(result.Warnings);
    }

    [Theory]
    [InlineData("/NFL /NJH", true)]
    [InlineData("/NJS", true)]
    [InlineData("/NFL /NDL", false)]
    public void Build_WarnsWhenExtraRawArgumentsSuppressSummaryStats_ButNeverStripsThem(string extra, bool expectWarning)
    {
        var options = Minimal() with { ExtraRawArguments = extra };
        var result = RobocopyArgumentBuilder.Build(options, "S", "D", [], [], "log.log");

        Assert.Equal(expectWarning, result.Warnings.Count > 0);
        // The user's own intent is always preserved in the argument list, regardless of the warning.
        foreach (var token in RobocopyArgumentBuilder.TokenizeRawArguments(extra))
        {
            Assert.Contains(token, result.Arguments);
        }
    }

    [Fact]
    public void Build_UsesArgumentListStyleTokens_NotAHandAssembledString()
    {
        // A source path containing spaces must survive as one array element, proving the
        // caller can safely hand this straight to ProcessStartInfo.ArgumentList.
        var result = RobocopyArgumentBuilder.Build(Minimal(), @"D:\My Music", @"E:\Backup", [], [], "log.log");

        Assert.Equal(@"D:\My Music", result.Arguments[0]);
    }

    [Fact]
    public void Build_TrailingBackslashOnSpaceContainingPath_IsStripped()
    {
        // Regression test for a real-world exit-code-16 failure: Robocopy's own argument parser
        // misreads a quoted path ending in "\" immediately before the closing quote (the \"
        // sequence looks like an escaped literal quote, not end-of-string), so a space-containing
        // path with a trailing backslash breaks even though .NET's ArgumentList quoting is correct.
        var result = RobocopyArgumentBuilder.Build(
            Minimal(),
            @"\\synology-nas-jo.localdomain\Backups\gitlab\",
            @"D:\Servers Backup\gitlab\",
            [], [], "log.log");

        Assert.Equal(@"\\synology-nas-jo.localdomain\Backups\gitlab", result.Arguments[0]);
        Assert.Equal(@"D:\Servers Backup\gitlab", result.Arguments[1]);
    }

    [Fact]
    public void Build_BareDriveRoot_KeepsTrailingBackslash()
    {
        // "D:" alone means "current directory on drive D:", not its root — stripping the
        // backslash here would silently change what the argument means to Robocopy.
        var result = RobocopyArgumentBuilder.Build(Minimal(), @"D:\", @"E:\", [], [], "log.log");

        Assert.Equal(@"D:\", result.Arguments[0]);
        Assert.Equal(@"E:\", result.Arguments[1]);
    }

    [Fact]
    public void Build_BareDriveLetterWithNoTrailingBackslash_IsLeftAlone()
    {
        // Regression test: "D:" (no trailing backslash at all) means "current directory on
        // drive D:" to Windows — a prior version of the sanitizer re-appended a backslash
        // here based only on the trimmed string's shape, silently turning it into "D:\" (the
        // drive root), which is a materially different and broader target.
        var result = RobocopyArgumentBuilder.Build(Minimal(), "D:", "E:", [], [], "log.log");

        Assert.Equal("D:", result.Arguments[0]);
        Assert.Equal("E:", result.Arguments[1]);
    }

    [Theory]
    [InlineData(@"\")]
    [InlineData(@"\\")]
    [InlineData(@"\\\")]
    public void Build_AllBackslashPath_ThrowsInsteadOfProducingEmptyArgument(string allBackslashPath)
    {
        // Regression test: TrimEnd('\') on a path that's nothing but backslashes produces an
        // empty string, which isn't caught by ThrowIfNullOrWhiteSpace (backslash isn't
        // whitespace) — an empty positional argument must never silently reach robocopy.exe,
        // especially for a job that may run /MIR or /PURGE.
        Assert.Throws<ArgumentException>(() =>
            RobocopyArgumentBuilder.Build(Minimal(), allBackslashPath, @"D:\Backup", [], [], "log.log"));

        Assert.Throws<ArgumentException>(() =>
            RobocopyArgumentBuilder.Build(Minimal(), @"D:\Source", allBackslashPath, [], [], "log.log"));
    }
}
