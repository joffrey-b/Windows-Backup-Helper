namespace WindowsBackupHelper.Core.Notifications;

/// <summary>
/// Abstracts end-of-job notifications. The real NotifyIcon-based implementation lands in a
/// later phase; JobExecutionService only needs the seam.
/// </summary>
public interface INotificationService
{
    void NotifyJobCompleted(string jobName, bool success, string summary);
}

/// <summary>Default no-op implementation — used until a real notification UI is wired up.</summary>
public sealed class NoOpNotificationService : INotificationService
{
    public void NotifyJobCompleted(string jobName, bool success, string summary)
    {
    }
}
