using Microsoft.Win32.TaskScheduler;
using WindowsBackupHelper.Core.Scheduling;

namespace WindowsBackupHelper.Win.Scheduling;

/// <summary>
/// ITaskSchedulerService backed by the real Windows Task Scheduler (Microsoft.Win32.TaskScheduler
/// package). Tasks live under a dedicated \WindowsBackupHelper\ folder, Action is the published
/// exe with "--run-job {jobId} --headless" and an explicit working directory (scheduled tasks
/// otherwise default to %SystemRoot%\System32).
/// </summary>
public sealed class WindowsTaskSchedulerService : ITaskSchedulerService
{
    private const string TaskFolderName = "WindowsBackupHelper";

    public void RegisterOrUpdateTask(
        string taskName, string jobId, ScheduleTriggerInfo trigger, bool isEnabled,
        bool runWhetherUserLoggedOnOrNot, string? windowsAccountPassword = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskName);
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);

        using var taskService = new TaskService();
        var folder = taskService.RootFolder.SubFolders.Exists(TaskFolderName)
            ? taskService.RootFolder.SubFolders[TaskFolderName]
            : taskService.RootFolder.CreateFolder(TaskFolderName);

        var taskDefinition = taskService.NewTask();
        taskDefinition.RegistrationInfo.Description = $"Windows Backup Helper scheduled job {jobId}";
        taskDefinition.Settings.Enabled = isEnabled;

        taskDefinition.Triggers.Clear();
        taskDefinition.Triggers.Add(BuildTrigger(trigger));

        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the current executable path.");
        taskDefinition.Actions.Clear();
        taskDefinition.Actions.Add(new ExecAction(exePath, $"--run-job {jobId} --headless", Path.GetDirectoryName(exePath)));

        // v1 default: InteractiveToken (user must be logged in, full network access, no extra
        // secret needed). Password logon is a fully-unattended advanced opt-in requiring the
        // user's actual Windows account password at registration time (stored thereafter by
        // Windows itself, not this app). S4U cannot reach network shares at all — a documented
        // Windows limitation — so it is never offered here.
        taskDefinition.Principal.LogonType = runWhetherUserLoggedOnOrNot ? TaskLogonType.Password : TaskLogonType.InteractiveToken;

        if (runWhetherUserLoggedOnOrNot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(windowsAccountPassword);
            folder.RegisterTaskDefinition(
                taskName, taskDefinition, TaskCreation.CreateOrUpdate,
                Environment.UserName, windowsAccountPassword, TaskLogonType.Password);
        }
        else
        {
            folder.RegisterTaskDefinition(taskName, taskDefinition);
        }
    }

    public void DeleteTask(string taskName)
    {
        using var taskService = new TaskService();
        var folder = taskService.RootFolder.SubFolders.Exists(TaskFolderName)
            ? taskService.RootFolder.SubFolders[TaskFolderName]
            : null;
        folder?.DeleteTask(taskName, exceptionOnNotExists: false);
    }

    public LiveTaskInfo? GetLiveTaskInfo(string taskName)
    {
        using var taskService = new TaskService();
        var task = taskService.GetTask($@"\{TaskFolderName}\{taskName}");
        if (task is null)
        {
            return null;
        }

        return new LiveTaskInfo(
            taskName,
            task.Definition.Triggers.Count > 0 ? task.Definition.Triggers[0].ToString() : null,
            task.Enabled,
            task.LastRunTime == DateTime.MinValue ? null : task.LastRunTime,
            task.LastTaskResult);
    }

    private static Trigger BuildTrigger(ScheduleTriggerInfo trigger)
    {
        var startBoundary = DateTime.Today.Add(trigger.TimeOfDay);

        return trigger.Frequency switch
        {
            ScheduleFrequency.Daily => new DailyTrigger { StartBoundary = startBoundary },
            ScheduleFrequency.Weekly => new WeeklyTrigger
            {
                StartBoundary = startBoundary,
                DaysOfWeek = trigger.DaysOfWeek.Aggregate(
                    (DaysOfTheWeek)0, (accumulated, day) => accumulated | ToDaysOfTheWeek(day)),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(trigger)),
        };
    }

    private static DaysOfTheWeek ToDaysOfTheWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Sunday => DaysOfTheWeek.Sunday,
        DayOfWeek.Monday => DaysOfTheWeek.Monday,
        DayOfWeek.Tuesday => DaysOfTheWeek.Tuesday,
        DayOfWeek.Wednesday => DaysOfTheWeek.Wednesday,
        DayOfWeek.Thursday => DaysOfTheWeek.Thursday,
        DayOfWeek.Friday => DaysOfTheWeek.Friday,
        DayOfWeek.Saturday => DaysOfTheWeek.Saturday,
        _ => throw new ArgumentOutOfRangeException(nameof(day)),
    };
}
