using System.Globalization;
using System.Text.RegularExpressions;

namespace WindowsBackupHelper.Core.Robocopy;

/// <summary>Total/Copied/Skipped/Mismatch/Failed/Extras for one summary-block row (Dirs, Files, or Bytes).</summary>
public sealed record RobocopyCategoryTotals(long Total, long Copied, long Skipped, long Mismatch, long Failed, long Extras);

public sealed class RobocopySummary
{
    public RobocopyCategoryTotals? Dirs { get; init; }
    public RobocopyCategoryTotals? Files { get; init; }
    public RobocopyCategoryTotals? Bytes { get; init; }
    public double? AverageSpeedBytesPerSec { get; init; }
}

/// <summary>
/// Parses Robocopy's fixed-shape summary block. The row labels ("Dirs :", "Files :", etc.)
/// and units are locale-dependent, so this is display-only/approximate — the numeric exit
/// code (locale-independent) is the sole authoritative pass/fail signal
/// (see RobocopyExitCodeInterpreter).
/// </summary>
public static partial class RobocopyOutputParser
{
    [GeneratedRegex(@"^\s*(?<label>Dirs|Files|Bytes)\s*:\s*(?<rest>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex CategoryLineRegex();

    [GeneratedRegex(@"(?<num>\d+(?:\.\d+)?)\s*(?<unit>[kKmMgGtT])?")]
    private static partial Regex ColumnValueRegex();

    [GeneratedRegex(@"^\s*Speed\s*:\s*(?<num>[\d.]+)\s*Bytes/sec", RegexOptions.IgnoreCase)]
    private static partial Regex SpeedBytesPerSecRegex();

    [GeneratedRegex(@"^-{10,}$")]
    private static partial Regex SummaryBlockSeparatorRegex();

    public static RobocopySummary Parse(string robocopyOutput)
    {
        ArgumentNullException.ThrowIfNull(robocopyOutput);

        RobocopyCategoryTotals? dirs = null;
        RobocopyCategoryTotals? files = null;
        RobocopyCategoryTotals? bytes = null;
        double? speed = null;

        // Fallback for non-English Windows installs: Robocopy's row LABELS ("Dirs :",
        // "Files :", "Bytes :") come from the OS's display-language resources, so e.g. a
        // French install prints "Répertoires :"/"Fichiers :"/"Octets :" instead — the regex
        // above never matches those. But the summary block's row ORDER (Dirs, then Files,
        // then Bytes) is always the same regardless of language, so track data rows
        // positionally within the last "----" separated block too, and backfill whichever
        // categories the label regex missed.
        var positionalRows = new List<RobocopyCategoryTotals>();

        foreach (var rawLine in robocopyOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (SummaryBlockSeparatorRegex().IsMatch(line))
            {
                positionalRows.Clear();
                continue;
            }

            var categoryMatch = CategoryLineRegex().Match(line);
            var rest = categoryMatch.Success ? categoryMatch.Groups["rest"].Value : GetRestAfterColon(line);
            var totals = rest is not null ? ParseCategoryLine(rest) : null;

            if (totals is not null)
            {
                if (categoryMatch.Success)
                {
                    switch (categoryMatch.Groups["label"].Value.ToLowerInvariant())
                    {
                        case "dirs": dirs = totals; break;
                        case "files": files = totals; break;
                        case "bytes": bytes = totals; break;
                    }
                }

                if (positionalRows.Count < 3)
                {
                    positionalRows.Add(totals);
                }

                continue;
            }

            var speedMatch = SpeedBytesPerSecRegex().Match(line);
            if (speedMatch.Success &&
                double.TryParse(speedMatch.Groups["num"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedSpeed))
            {
                speed = parsedSpeed;
            }
        }

        if (positionalRows.Count >= 1) dirs ??= positionalRows[0];
        if (positionalRows.Count >= 2) files ??= positionalRows[1];
        if (positionalRows.Count >= 3) bytes ??= positionalRows[2];

        return new RobocopySummary { Dirs = dirs, Files = files, Bytes = bytes, AverageSpeedBytesPerSec = speed };
    }

    private static string? GetRestAfterColon(string line)
    {
        var colonIndex = line.IndexOf(':');
        return colonIndex >= 0 ? line[(colonIndex + 1)..] : null;
    }

    private static RobocopyCategoryTotals? ParseCategoryLine(string rest)
    {
        var matches = ColumnValueRegex().Matches(rest);
        if (matches.Count < 6)
        {
            return null;
        }

        var values = new long[6];
        for (var i = 0; i < 6; i++)
        {
            values[i] = ToApproximateLong(matches[i]);
        }

        return new RobocopyCategoryTotals(values[0], values[1], values[2], values[3], values[4], values[5]);
    }

    private static long ToApproximateLong(Match match)
    {
        var number = double.Parse(match.Groups["num"].Value, CultureInfo.InvariantCulture);
        var multiplier = match.Groups["unit"].Success ? char.ToLowerInvariant(match.Groups["unit"].Value[0]) switch
        {
            'k' => 1_000d,
            'm' => 1_000_000d,
            'g' => 1_000_000_000d,
            't' => 1_000_000_000_000d,
            _ => 1d,
        } : 1d;

        return (long)Math.Round(number * multiplier);
    }
}
