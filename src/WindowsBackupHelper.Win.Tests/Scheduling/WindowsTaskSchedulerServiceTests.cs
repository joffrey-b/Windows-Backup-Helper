using WindowsBackupHelper.Core.Scheduling;
using WindowsBackupHelper.Win.Scheduling;

namespace WindowsBackupHelper.Win.Tests.Scheduling;

/// <summary>
/// Exercises the real Windows Task Scheduler on this machine (not a fake) — there is no safe
/// way to unit test task registration without touching the real OS API. Every test uses an
/// obviously test-scoped task name and cleans up in a finally block.
/// </summary>
public sealed class WindowsTaskSchedulerServiceTests
{
    private static string NewTestTaskName() => $"WbhTest_{Guid.NewGuid():N}";

    [Fact]
    public void RegisterOrUpdateTask_Daily_CreatesATaskMatchingWhatWasRequested()
    {
        var service = new WindowsTaskSchedulerService();
        var taskName = NewTestTaskName();
        var trigger = new ScheduleTriggerInfo(ScheduleFrequency.Daily, new TimeSpan(3, 30, 0), []);

        service.RegisterOrUpdateTask(taskName, "job-123", trigger, isEnabled: true, runWhetherUserLoggedOnOrNot: false);
        try
        {
            var liveInfo = service.GetLiveTaskInfo(taskName);

            Assert.NotNull(liveInfo);
            Assert.True(liveInfo!.IsEnabled);
            Assert.Contains("3:30", liveInfo.TriggerDescription);
        }
        finally
        {
            service.DeleteTask(taskName);
        }
    }

    [Fact]
    public void RegisterOrUpdateTask_Disabled_CreatesADisabledTask()
    {
        var service = new WindowsTaskSchedulerService();
        var taskName = NewTestTaskName();
        var trigger = new ScheduleTriggerInfo(ScheduleFrequency.Daily, new TimeSpan(2, 0, 0), []);

        service.RegisterOrUpdateTask(taskName, "job-123", trigger, isEnabled: false, runWhetherUserLoggedOnOrNot: false);
        try
        {
            var liveInfo = service.GetLiveTaskInfo(taskName);

            Assert.NotNull(liveInfo);
            Assert.False(liveInfo!.IsEnabled);
        }
        finally
        {
            service.DeleteTask(taskName);
        }
    }

    [Fact]
    public void RegisterOrUpdateTask_ActionPointsAtTheCurrentExecutable_WithRunJobAndHeadlessArgs()
    {
        var service = new WindowsTaskSchedulerService();
        var taskName = NewTestTaskName();
        var trigger = new ScheduleTriggerInfo(ScheduleFrequency.Daily, new TimeSpan(2, 0, 0), []);

        service.RegisterOrUpdateTask(taskName, "job-abc", trigger, isEnabled: true, runWhetherUserLoggedOnOrNot: false);
        try
        {
            using var taskService = new Microsoft.Win32.TaskScheduler.TaskService();
            var task = taskService.GetTask($@"\WindowsBackupHelper\{taskName}");
            Assert.NotNull(task);

            var action = Assert.IsType<Microsoft.Win32.TaskScheduler.ExecAction>(task!.Definition.Actions[0]);
            Assert.Equal(Environment.ProcessPath, action.Path);
            Assert.Contains("--run-job job-abc", action.Arguments);
            Assert.Contains("--headless", action.Arguments);
            Assert.False(string.IsNullOrEmpty(action.WorkingDirectory));
        }
        finally
        {
            service.DeleteTask(taskName);
        }
    }

    [Fact]
    public void RegisterOrUpdateTask_DefaultsToInteractiveTokenLogon_NotS4UWhichCannotReachNetworkShares()
    {
        if (!Environment.UserInteractive)
        {
            // Windows Task Scheduler only honors InteractiveToken logon for principals that are
            // themselves interactively logged on -- registering from a non-interactive session
            // (e.g. a CI runner running as a Windows service/SYSTEM account) gets silently
            // coerced to ServiceAccount instead. That's Windows' own behavior, not something
            // this app's code controls, so there's nothing meaningful to assert outside a real
            // interactive desktop session (which is how the app is actually used).
            return;
        }

        var service = new WindowsTaskSchedulerService();
        var taskName = NewTestTaskName();
        var trigger = new ScheduleTriggerInfo(ScheduleFrequency.Daily, new TimeSpan(2, 0, 0), []);

        service.RegisterOrUpdateTask(taskName, "job-123", trigger, isEnabled: true, runWhetherUserLoggedOnOrNot: false);
        try
        {
            using var taskService = new Microsoft.Win32.TaskScheduler.TaskService();
            var task = taskService.GetTask($@"\WindowsBackupHelper\{taskName}");
            Assert.Equal(Microsoft.Win32.TaskScheduler.TaskLogonType.InteractiveToken, task!.Definition.Principal.LogonType);
        }
        finally
        {
            service.DeleteTask(taskName);
        }
    }

    [Fact]
    public void RegisterOrUpdateTask_CalledTwiceWithSameName_UpdatesRatherThanDuplicating()
    {
        var service = new WindowsTaskSchedulerService();
        var taskName = NewTestTaskName();

        service.RegisterOrUpdateTask(
            taskName, "job-123", new ScheduleTriggerInfo(ScheduleFrequency.Daily, new TimeSpan(2, 0, 0), []),
            isEnabled: true, runWhetherUserLoggedOnOrNot: false);
        try
        {
            service.RegisterOrUpdateTask(
                taskName, "job-123", new ScheduleTriggerInfo(ScheduleFrequency.Daily, new TimeSpan(5, 0, 0), []),
                isEnabled: true, runWhetherUserLoggedOnOrNot: false);

            var liveInfo = service.GetLiveTaskInfo(taskName);
            Assert.Contains("5:00", liveInfo!.TriggerDescription);
        }
        finally
        {
            service.DeleteTask(taskName);
        }
    }

    [Fact]
    public void GetLiveTaskInfo_NonexistentTask_ReturnsNull()
    {
        var service = new WindowsTaskSchedulerService();

        Assert.Null(service.GetLiveTaskInfo(NewTestTaskName()));
    }

    [Fact]
    public void DeleteTask_NonexistentTask_DoesNotThrow()
    {
        var service = new WindowsTaskSchedulerService();

        var exception = Record.Exception(() => service.DeleteTask(NewTestTaskName()));

        Assert.Null(exception);
    }

    [Fact]
    public void DeleteTask_RemovesTheTask_SoASubsequentLookupReturnsNull()
    {
        var service = new WindowsTaskSchedulerService();
        var taskName = NewTestTaskName();
        service.RegisterOrUpdateTask(
            taskName, "job-123", new ScheduleTriggerInfo(ScheduleFrequency.Daily, new TimeSpan(2, 0, 0), []),
            isEnabled: true, runWhetherUserLoggedOnOrNot: false);

        service.DeleteTask(taskName);

        Assert.Null(service.GetLiveTaskInfo(taskName));
    }

    [Fact]
    public void RegisterOrUpdateTask_Weekly_SetsTheRequestedDays()
    {
        var service = new WindowsTaskSchedulerService();
        var taskName = NewTestTaskName();
        var trigger = new ScheduleTriggerInfo(ScheduleFrequency.Weekly, new TimeSpan(1, 0, 0), [DayOfWeek.Monday, DayOfWeek.Friday]);

        service.RegisterOrUpdateTask(taskName, "job-123", trigger, isEnabled: true, runWhetherUserLoggedOnOrNot: false);
        try
        {
            using var taskService = new Microsoft.Win32.TaskScheduler.TaskService();
            var task = taskService.GetTask($@"\WindowsBackupHelper\{taskName}");
            var weeklyTrigger = Assert.IsType<Microsoft.Win32.TaskScheduler.WeeklyTrigger>(task!.Definition.Triggers[0]);

            Assert.True(weeklyTrigger.DaysOfWeek.HasFlag(Microsoft.Win32.TaskScheduler.DaysOfTheWeek.Monday));
            Assert.True(weeklyTrigger.DaysOfWeek.HasFlag(Microsoft.Win32.TaskScheduler.DaysOfTheWeek.Friday));
            Assert.False(weeklyTrigger.DaysOfWeek.HasFlag(Microsoft.Win32.TaskScheduler.DaysOfTheWeek.Tuesday));
        }
        finally
        {
            service.DeleteTask(taskName);
        }
    }
}
