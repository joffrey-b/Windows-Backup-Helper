using WindowsBackupHelper.Core.Notifications;

namespace WindowsBackupHelper.Core.Tests.Execution;

public sealed class FakeNotificationService : INotificationService
{
    public List<(string JobName, bool Success, string Summary)> Notifications { get; } = [];

    public void NotifyJobCompleted(string jobName, bool success, string summary) => Notifications.Add((jobName, success, summary));
}
