using System.Text;

namespace WindowsBackupHelper.Core.Robocopy;

/// <summary>Ready-to-use ArgumentList entries, plus any warnings about the user's own passthrough flags.</summary>
public sealed record RobocopyCommandLine(IReadOnlyList<string> Arguments, IReadOnlyList<string> Warnings);

/// <summary>
/// Builds a Robocopy argument list as discrete tokens for ProcessStartInfo.ArgumentList —
/// never a hand-assembled Arguments string, which sidesteps quoting bugs with UNC paths and
/// space-containing folder names.
/// </summary>
public static class RobocopyArgumentBuilder
{
    public static RobocopyCommandLine Build(
        ResolvedRobocopyOptions options,
        string source,
        string destination,
        IReadOnlyList<string> resolvedExcludeFiles,
        IReadOnlyList<string> resolvedExcludeDirs,
        string logFilePath,
        bool dryRun = false)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

        var sanitizedSource = SanitizePathArgument(source);
        var sanitizedDestination = SanitizePathArgument(destination);

        // A path of nothing but backslashes (e.g. "\\") trims down to an empty string —
        // not caught by the whitespace check above since backslash isn't whitespace, but not
        // a meaningful Robocopy target either.
        if (string.IsNullOrWhiteSpace(sanitizedSource))
        {
            throw new ArgumentException("Source path is not a valid Robocopy path.", nameof(source));
        }

        if (string.IsNullOrWhiteSpace(sanitizedDestination))
        {
            throw new ArgumentException("Destination path is not a valid Robocopy path.", nameof(destination));
        }

        var args = new List<string> { sanitizedSource, sanitizedDestination };

        if (options.Mirror) args.Add("/MIR");
        if (options.CopySubdirectories) args.Add("/S");
        if (options.CopyEmptySubdirectories) args.Add("/E");
        if (options.Purge) args.Add("/PURGE");
        if (options.Move) args.Add("/MOVE");
        if (options.MoveFilesOnly) args.Add("/MOV");
        if (!string.IsNullOrWhiteSpace(options.CopyFlags)) args.Add($"/COPY:{options.CopyFlags}");
        if (!string.IsNullOrWhiteSpace(options.DirectoryCopyFlags)) args.Add($"/DCOPY:{options.DirectoryCopyFlags}");
        if (options.CopyAll) args.Add("/COPYALL");
        if (options.IncludeSecurity) args.Add("/SEC");
        if (options.Restartable) args.Add("/Z");
        if (options.BackupMode) args.Add("/B");
        if (options.RestartableBackupMode) args.Add("/ZB");
        if (options.CopySymlinksAsLinks) args.Add("/SL");
        if (options.ArchiveOnly) args.Add("/A");
        if (options.ArchiveOnlyAndReset) args.Add("/M");
        if (!string.IsNullOrWhiteSpace(options.IncludeAttributeFilter)) args.Add($"/IA:{options.IncludeAttributeFilter}");
        if (!string.IsNullOrWhiteSpace(options.ExcludeAttributeFilter)) args.Add($"/XA:{options.ExcludeAttributeFilter}");
        if (!string.IsNullOrWhiteSpace(options.MinFileAge)) args.Add($"/MINAGE:{options.MinFileAge}");
        if (!string.IsNullOrWhiteSpace(options.MaxFileAge)) args.Add($"/MAXAGE:{options.MaxFileAge}");
        if (options.MinFileSizeBytes is { } minSize) args.Add($"/MIN:{minSize}");
        if (options.MaxFileSizeBytes is { } maxSize) args.Add($"/MAX:{maxSize}");
        if (options.ExcludeOlder) args.Add("/XO");
        if (options.ExcludeNewer) args.Add("/XN");
        if (options.ExcludeChanged) args.Add("/XC");
        if (options.ExcludeExtra) args.Add("/XX");
        if (options.MultithreadCount is { } threads) args.Add($"/MT:{threads}");

        // Hard requirement: Robocopy's own defaults (/R:1000000 /W:30) are effectively
        // "hang forever" on one unreachable file. ResolvedRobocopyOptions guarantees these
        // are non-null, so they are always emitted explicitly.
        args.Add($"/R:{options.Retries}");
        args.Add($"/W:{options.WaitSeconds}");

        // Always suppress the per-file percent-copied indicator. On a real console these
        // repeated updates overwrite each other in place via a bare \r, but /UNILOG has no
        // such rendering — each update gets appended to the same log line instead, so a
        // single large file balloons into one enormous line packed with dozens of "12.3%
        // 14.6% ..." fragments. /NP keeps the log to one clean line per file/dir and is also
        // what makes counting completed items for live progress reporting reliable.
        args.Add("/NP");

        if (options.FatFileTimestampTolerance) args.Add("/FFT");
        if (options.AssumeFatDst) args.Add("/DST");
        if (options.Verbose) args.Add("/V");

        if (resolvedExcludeFiles.Count > 0)
        {
            args.Add("/XF");
            args.AddRange(resolvedExcludeFiles);
        }

        if (resolvedExcludeDirs.Count > 0)
        {
            args.Add("/XD");
            args.AddRange(resolvedExcludeDirs);
        }

        if (dryRun) args.Add("/L");

        var warnings = new List<string>();
        if (!string.IsNullOrWhiteSpace(options.ExtraRawArguments))
        {
            var extraTokens = TokenizeRawArguments(options.ExtraRawArguments);
            args.AddRange(extraTokens);

            if (extraTokens.Any(t => t.Equals("/NJH", StringComparison.OrdinalIgnoreCase) || t.Equals("/NJS", StringComparison.OrdinalIgnoreCase)))
            {
                warnings.Add("ExtraRawArguments includes /NJH or /NJS — Robocopy's summary statistics may be incomplete.");
            }
        }

        // Always append a managed /UNILOG(+) pointing at an app-owned per-run log path.
        // /UNILOG round-trips Unicode filenames correctly (real-world music libraries have
        // non-ASCII artist/album names); plain console stdout doesn't reliably.
        args.Add(options.AppendToLog ? $"/UNILOG+:{logFilePath}" : $"/UNILOG:{logFilePath}");

        return new RobocopyCommandLine(args, warnings);
    }

    /// <summary>
    /// Splits a user-typed passthrough string (e.g. "/XD \"My Folder\" /NFL") into discrete
    /// argument tokens, honoring double-quoted groups so a single quoted argument containing
    /// spaces isn't split apart.
    /// </summary>
    public static IReadOnlyList<string> TokenizeRawArguments(string raw)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        foreach (var ch in raw)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (char.IsWhiteSpace(ch) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                }
                continue;
            }

            current.Append(ch);
        }

        if (current.Length > 0)
        {
            tokens.Add(current.ToString());
        }

        return tokens;
    }

    /// <summary>
    /// Strips a trailing backslash from a source/destination path, unless it's a bare drive
    /// root (e.g. "C:\"). Robocopy has a well-known parsing bug where a quoted path argument
    /// ending in "\" immediately before the closing quote is misread — the OS argument parser
    /// treats \" as an escaped literal quote rather than end-of-string, so Robocopy never sees
    /// where the path actually ends. This only bites when the path is quoted (i.e. it contains
    /// a space), which is exactly the case .NET's own correct ArgumentList quoting can't avoid
    /// since Robocopy's argument parser — not the OS's — has the bug. Dropping the trailing
    /// backslash sidesteps it entirely; "C:" alone means "current directory on drive C:" rather
    /// than its root, so that one case must keep the backslash.
    /// </summary>
    private static string SanitizePathArgument(string path)
    {
        if (!path.EndsWith('\\'))
        {
            // Nothing to strip — in particular, a bare "C:" (no backslash at all) must be
            // left alone rather than "corrected" into "C:\", since those mean different things.
            return path;
        }

        var trimmed = path.TrimEnd('\\');

        if (trimmed.Length == 2 && trimmed[1] == ':')
        {
            return trimmed + '\\';
        }

        return trimmed;
    }
}
