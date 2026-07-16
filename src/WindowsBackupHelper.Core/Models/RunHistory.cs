namespace WindowsBackupHelper.Core.Models;

public enum RunTriggerType
{
    Manual,
    Scheduled,
    Cli,
}

public enum RunOutcome
{
    Success,
    SuccessWithMismatches,
    Failed,
    Cancelled,
    PartialFailure,
}

/// <summary>
/// One row per "run job" or "run one pair" trigger. A whole-job run produces N child
/// FolderPairRunResult rows; a single-pair run produces exactly 1.
/// </summary>
public sealed class RunHistory
{
    public int Id { get; set; }
    public required string JobId { get; set; }
    public required RunTriggerType TriggerType { get; set; }
    public required DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
    public bool WasDryRun { get; set; }
    public RunOutcome? OverallOutcome { get; set; }
    public string? Notes { get; set; }
}
