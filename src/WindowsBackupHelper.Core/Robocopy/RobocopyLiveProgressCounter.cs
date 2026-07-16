namespace WindowsBackupHelper.Core.Robocopy;

/// <summary>
/// Counts completed file entries in a growing /UNILOG file, for a live "N files copied so
/// far" progress display while a run is still in flight. This is a live-only, approximate
/// signal — the persisted Files/Bytes-copied counts always come from
/// RobocopyOutputParser's authoritative final summary block once the run finishes,
/// independent of this counter.
///
/// Requires /NP (always applied by RobocopyArgumentBuilder): without it, a single large
/// file's repeated percent-copied updates get appended onto one enormous log line instead of
/// terminating it, which would make line-based counting meaningless.
/// </summary>
public static class RobocopyLiveProgressCounter
{
    /// <summary>
    /// Robocopy logs each directory/file entry as one tab-separated line, ending in the full
    /// path for directories (trailing "\") or just the filename for files (no trailing "\").
    /// Entries only start after the 3rd "----" separator (banner start, banner end, options
    /// end) — anything before that is header/job-info text, not a real entry.
    /// </summary>
    public static int CountCompletedFiles(string logContent)
    {
        ArgumentNullException.ThrowIfNull(logContent);

        var count = 0;
        var separatorCount = 0;

        foreach (var rawLine in logContent.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (IsSeparatorLine(line))
            {
                separatorCount++;
                continue;
            }

            if (separatorCount < 3 || string.IsNullOrWhiteSpace(line) || !line.Contains('\t'))
            {
                continue;
            }

            var segments = line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            var lastSegment = segments[^1];
            if (!lastSegment.EndsWith('\\') && !lastSegment.EndsWith('/'))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsSeparatorLine(string line) => line.Length >= 10 && line.All(c => c == '-');
}
