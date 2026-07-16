namespace WindowsBackupHelper.Core.Flac;

public enum FlacFileStatus
{
    Ok,
    Warning,
    Error,
}

/// <summary>The raw result of spawning `flac -t --silent &lt;path&gt;` for one file.</summary>
public sealed record FlacProcessResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>FlacResultClassifier's verdict, before it's paired with a reporting path.</summary>
public sealed record FlacClassification(FlacFileStatus Status, IReadOnlyList<string> Messages);

/// <summary>One classified file, ready for the audit report.</summary>
public sealed record FlacFileResult(string RelativePath, FlacFileStatus Status, IReadOnlyList<string> Messages);

/// <summary>
/// Thrown when the configured flac executable can't be found or started. The UI should turn
/// this into a pointer at AppSettings.FlacExecutablePath rather than a generic error.
/// </summary>
public sealed class FlacExecutableNotFoundException(string flacExecutablePath, Exception? innerException = null)
    : Exception(
        $"The FLAC executable was not found at '{flacExecutablePath}'. " +
        "Set AppSettings.FlacExecutablePath to a valid flac.exe path.",
        innerException)
{
    public string FlacExecutablePath { get; } = flacExecutablePath;
}
