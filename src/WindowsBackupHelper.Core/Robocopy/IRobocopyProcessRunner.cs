namespace WindowsBackupHelper.Core.Robocopy;

public sealed record RobocopyProcessResult(int ExitCode, string StandardOutput);

/// <summary>Abstracts spawning robocopy.exe so JobExecutionService is testable without a real process.</summary>
public interface IRobocopyProcessRunner
{
    Task<RobocopyProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default);
}
