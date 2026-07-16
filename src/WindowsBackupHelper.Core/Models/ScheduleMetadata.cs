namespace WindowsBackupHelper.Core.Models;

/// <summary>
/// Display cache for a job's Task Scheduler registration — the OS task is always the
/// authoritative source. LastSyncedUtc drives drift detection against manual edits
/// made directly in Task Scheduler.
/// </summary>
public sealed class ScheduleMetadata
{
    public int Id { get; set; }
    public required string JobId { get; set; }
    public required string TaskSchedulerTaskName { get; set; }
    public string? TriggerDescription { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool RunWhetherUserLoggedOnOrNot { get; set; }
    public DateTime? LastSyncedUtc { get; set; }
}
