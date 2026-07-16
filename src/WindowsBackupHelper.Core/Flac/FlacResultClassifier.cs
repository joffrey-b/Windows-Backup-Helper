namespace WindowsBackupHelper.Core.Flac;

/// <summary>
/// Ports flac_audit_windows_linux.py's classification logic as a pure function: non-zero
/// exit -> Error (messages = non-empty output lines not starting with the invoked file path,
/// "Unknown error" fallback); zero exit but combined stdout+stderr contains "cannot check MD5"
/// or "WARNING" -> Warning (valid audio, but no MD5 signature in STREAMINFO — future
/// corruption undetectable for that file); otherwise -> Ok.
/// </summary>
public static class FlacResultClassifier
{
    public static FlacClassification Classify(FlacProcessResult processResult, string filePath)
    {
        ArgumentNullException.ThrowIfNull(processResult);

        var stdout = processResult.StandardOutput.Trim();
        var stderr = processResult.StandardError.Trim();
        var combined = string.Join('\n', new[] { stdout, stderr }.Where(s => s.Length > 0));

        if (processResult.ExitCode != 0)
        {
            var messages = NonEmptyTrimmedLines(combined)
                .Where(line => !line.StartsWith(filePath, StringComparison.Ordinal))
                .ToList();
            return new FlacClassification(FlacFileStatus.Error, messages.Count > 0 ? messages : ["Unknown error"]);
        }

        if (combined.Contains("cannot check MD5", StringComparison.Ordinal) || combined.Contains("WARNING", StringComparison.Ordinal))
        {
            var messages = NonEmptyTrimmedLines(combined)
                .Where(line => line.Contains("WARNING", StringComparison.Ordinal) || line.Contains("cannot check", StringComparison.Ordinal))
                .ToList();
            return new FlacClassification(FlacFileStatus.Warning, messages.Count > 0 ? messages : ["MD5 signature unset"]);
        }

        return new FlacClassification(FlacFileStatus.Ok, []);
    }

    private static IEnumerable<string> NonEmptyTrimmedLines(string text) =>
        text.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0);
}
