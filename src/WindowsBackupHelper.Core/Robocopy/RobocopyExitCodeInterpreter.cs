namespace WindowsBackupHelper.Core.Robocopy;

[Flags]
public enum RobocopyResultFlags
{
    None = 0,
    FilesCopied = 1 << 0,
    ExtraFilesOrDirectories = 1 << 1,
    MismatchedFilesOrDirectories = 1 << 2,
    CopyErrorsOccurred = 1 << 3,
    SeriousError = 1 << 4,
}

public enum RobocopySeverity
{
    NoChange,
    Success,
    SuccessWithExtrasOrMismatches,
    Failure,
    FatalError,
}

public sealed record RobocopyOutcome(int ExitCode, bool IsSuccess, RobocopySeverity Severity, RobocopyResultFlags FlagsSet, string HumanReadableSummary);

/// <summary>
/// Decodes Robocopy's exit code bitmask. This is the sole authoritative pass/fail signal —
/// unlike the summary-block text (see RobocopyOutputParser), the exit code is
/// locale-independent.
/// </summary>
public static class RobocopyExitCodeInterpreter
{
    public static RobocopyOutcome Interpret(int exitCode)
    {
        // A negative exit code means robocopy.exe crashed or was killed rather than exiting
        // normally through its own bitmask-encoded exit path -- `exitCode < 8` alone would
        // treat every negative value as success, and the bitmask below is meaningless for a
        // process that never got to set it.
        if (exitCode < 0)
        {
            return new RobocopyOutcome(
                exitCode, IsSuccess: false, RobocopySeverity.FatalError, RobocopyResultFlags.None,
                "Robocopy exited abnormally (crashed or was terminated) rather than completing normally.");
        }

        var flags = (RobocopyResultFlags)(exitCode & 0b1_1111);
        var isSuccess = exitCode < 8;

        var severity = flags switch
        {
            _ when flags.HasFlag(RobocopyResultFlags.SeriousError) => RobocopySeverity.FatalError,
            _ when flags.HasFlag(RobocopyResultFlags.CopyErrorsOccurred) => RobocopySeverity.Failure,
            RobocopyResultFlags.None => RobocopySeverity.NoChange,
            _ when flags.HasFlag(RobocopyResultFlags.ExtraFilesOrDirectories) || flags.HasFlag(RobocopyResultFlags.MismatchedFilesOrDirectories)
                => RobocopySeverity.SuccessWithExtrasOrMismatches,
            _ => RobocopySeverity.Success,
        };

        var parts = new List<string>();
        if (flags.HasFlag(RobocopyResultFlags.FilesCopied)) parts.Add("files were copied");
        if (flags.HasFlag(RobocopyResultFlags.ExtraFilesOrDirectories)) parts.Add("extra files/directories exist at the destination");
        if (flags.HasFlag(RobocopyResultFlags.MismatchedFilesOrDirectories)) parts.Add("mismatched files/directories were detected");
        if (flags.HasFlag(RobocopyResultFlags.CopyErrorsOccurred)) parts.Add("some files could not be copied");
        if (flags.HasFlag(RobocopyResultFlags.SeriousError)) parts.Add("a serious error occurred — Robocopy may not have copied anything; check the command line/parameters");

        var summary = parts.Count == 0
            ? "No changes were needed — source and destination already match."
            : string.Join("; ", parts) + ".";

        return new RobocopyOutcome(exitCode, isSuccess, severity, flags, summary);
    }
}
