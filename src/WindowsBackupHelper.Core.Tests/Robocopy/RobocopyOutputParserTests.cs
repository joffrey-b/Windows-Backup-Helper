using WindowsBackupHelper.Core.Robocopy;

namespace WindowsBackupHelper.Core.Tests.Robocopy;

public sealed class RobocopyOutputParserTests
{
    // A realistic full summary block as Robocopy actually emits it.
    private const string RealisticSummaryBlock = """

        -------------------------------------------------------------------------------

                       Total    Copied   Skipped  Mismatch    FAILED    Extras
            Dirs :        42        10        32         0         0         0
           Files :      1234       456       778         0         0         2
           Bytes :   12.34 g    5.67 g    6.67 g         0         0    123.4 k
           Times :   0:04:12   0:03:58                       0:00:00   0:00:14


           Speed :             8543210 Bytes/sec.
           Speed :             489.123 MegaBytes/min.

        """;

    [Fact]
    public void Parse_RealisticSummaryBlock_ExtractsDirsFilesAndBytesTotals()
    {
        var summary = RobocopyOutputParser.Parse(RealisticSummaryBlock);

        Assert.NotNull(summary.Dirs);
        Assert.Equal(42, summary.Dirs!.Total);
        Assert.Equal(10, summary.Dirs.Copied);
        Assert.Equal(32, summary.Dirs.Skipped);

        Assert.NotNull(summary.Files);
        Assert.Equal(1234, summary.Files!.Total);
        Assert.Equal(456, summary.Files.Copied);
        Assert.Equal(778, summary.Files.Skipped);
        Assert.Equal(2, summary.Files.Extras);
    }

    [Fact]
    public void Parse_UnitSuffixedByteValues_ApplyCorrectMultipliers()
    {
        var summary = RobocopyOutputParser.Parse(RealisticSummaryBlock);

        Assert.NotNull(summary.Bytes);
        Assert.Equal(12_340_000_000L, summary.Bytes!.Total); // 12.34 g
        Assert.Equal(5_670_000_000L, summary.Bytes.Copied);  // 5.67 g
        Assert.Equal(123_400L, summary.Bytes.Extras);        // 123.4 k
        Assert.Equal(0, summary.Bytes.Mismatch);              // plain "0", no unit
    }

    [Fact]
    public void Parse_SpeedLine_ExtractsBytesPerSecond_NotTheMegabytesPerMinuteLine()
    {
        var summary = RobocopyOutputParser.Parse(RealisticSummaryBlock);

        Assert.Equal(8_543_210d, summary.AverageSpeedBytesPerSec);
    }

    [Fact]
    public void Parse_ToleratesExtraWhitespaceAndColumnWidthVariance()
    {
        const string looselyFormatted = "Dirs :1  2  3  0  0  0\nFiles:  10   5   5    0   0   0\n";

        var summary = RobocopyOutputParser.Parse(looselyFormatted);

        Assert.NotNull(summary.Dirs);
        Assert.Equal(1, summary.Dirs!.Total);
        Assert.NotNull(summary.Files);
        Assert.Equal(10, summary.Files!.Total);
    }

    [Fact]
    public void Parse_MissingCategoryLine_LeavesThatPropertyNull_RatherThanThrowing()
    {
        var summary = RobocopyOutputParser.Parse("Some unrelated log noise\nwith no summary block at all");

        Assert.Null(summary.Dirs);
        Assert.Null(summary.Files);
        Assert.Null(summary.Bytes);
        Assert.Null(summary.AverageSpeedBytesPerSec);
    }

    [Fact]
    public void Parse_EmptyString_DoesNotThrow()
    {
        var summary = RobocopyOutputParser.Parse(string.Empty);
        Assert.Null(summary.Dirs);
    }

    // A non-English Windows install has Robocopy print localized row labels (French shown
    // here) instead of "Dirs :"/"Files :"/"Bytes :" — regression test for a real-world bug
    // where Files/Bytes copied showed up blank in the UI because the English-only label
    // regex never matched, even though the exit-code-derived outcome summary (which doesn't
    // depend on this parse) correctly reported files were copied.
    private const string FrenchLocaleSummaryBlock = """

        -------------------------------------------------------------------------------

                       Total    Copié(s)  Ignoré  Différent    ECHECS    En plus
        Répertoires :        42        10        32         0         0         0
          Fichiers :      1234       456       778         0         0         2
            Octets :   12.34 g    5.67 g    6.67 g         0         0    123.4 k
             Temps :   0:04:12   0:03:58                       0:00:00   0:00:14

        """;

    [Fact]
    public void Parse_LocalizedRowLabels_FallsBackToPositionalOrder()
    {
        var summary = RobocopyOutputParser.Parse(FrenchLocaleSummaryBlock);

        Assert.NotNull(summary.Dirs);
        Assert.Equal(42, summary.Dirs!.Total);
        Assert.Equal(10, summary.Dirs.Copied);

        Assert.NotNull(summary.Files);
        Assert.Equal(1234, summary.Files!.Total);
        Assert.Equal(456, summary.Files.Copied);

        Assert.NotNull(summary.Bytes);
        Assert.Equal(12_340_000_000L, summary.Bytes!.Total);
        Assert.Equal(5_670_000_000L, summary.Bytes.Copied);
    }

    [Fact]
    public void Parse_MultipleSeparatorSections_OnlyCountsRowsAfterTheLastOne()
    {
        // Robocopy's own log has separator lines around the header/options block AND before
        // the summary — positional counting must reset at each one so header noise (which can
        // itself contain colon-separated, 6-numeric-looking junk) never gets mistaken for the
        // real Dirs/Files/Bytes rows.
        const string withDecoySection = """

            -------------------------------------------------------------------------------
               Decoy : 1 2 3 4 5 6 7 8 9 10 11 12
            -------------------------------------------------------------------------------

                           Total    Copié(s)  Ignoré  Différent    ECHECS    En plus
            Répertoires :        1         1         0         0         0         0
              Fichiers :        2         2         0         0         0         0
                Octets :       300       300         0         0         0         0

            """;

        var summary = RobocopyOutputParser.Parse(withDecoySection);

        Assert.NotNull(summary.Dirs);
        Assert.Equal(1, summary.Dirs!.Total);
        Assert.NotNull(summary.Files);
        Assert.Equal(2, summary.Files!.Total);
        Assert.NotNull(summary.Bytes);
        Assert.Equal(300, summary.Bytes!.Total);
    }
}
