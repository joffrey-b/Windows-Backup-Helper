namespace WindowsBackupHelper.Core.Scheduling;

public enum ScheduleFrequency
{
    Daily,
    Weekly,
}

public sealed record ScheduleTriggerInfo(ScheduleFrequency Frequency, TimeSpan TimeOfDay, IReadOnlyList<DayOfWeek> DaysOfWeek);

/// <summary>The OS task's own state, for drift detection against ScheduleMetadata's cached fields.</summary>
public sealed record LiveTaskInfo(string TaskName, string? TriggerDescription, bool IsEnabled, DateTime? LastRunTime, int? LastTaskResult);

/// <summary>
/// Abstracts Windows Task Scheduler registration. Tasks live under a dedicated
/// \WindowsBackupHelper\ folder in the Task Scheduler library; the OS task is always the
/// authoritative source of truth, ScheduleMetadata is only a display cache.
/// </summary>
public interface ITaskSchedulerService
{
    /// <param name="windowsAccountPassword">
    /// Only required when runWhetherUserLoggedOnOrNot is true (Password logon type, a fully
    /// unattended but explicit advanced opt-in — S4U logon cannot reach network shares at
    /// all, so it's never offered, and InteractiveToken is the v1 default requiring no
    /// secret here at all).
    /// </param>
    void RegisterOrUpdateTask(
        string taskName, string jobId, ScheduleTriggerInfo trigger, bool isEnabled,
        bool runWhetherUserLoggedOnOrNot, string? windowsAccountPassword = null);

    void DeleteTask(string taskName);

    LiveTaskInfo? GetLiveTaskInfo(string taskName);
}
