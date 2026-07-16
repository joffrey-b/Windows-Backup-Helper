using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Scheduling;

namespace WindowsBackupHelper.App.ViewModels;

public sealed partial class ScheduleEditorViewModel : ObservableObject
{
    private readonly ITaskSchedulerService _taskSchedulerService;
    private readonly ScheduleMetadataRepository _scheduleMetadataRepository;
    private readonly Job _job;

    private ScheduleMetadata? _existing;

    [ObservableProperty]
    private ScheduleFrequency _frequency = ScheduleFrequency.Daily;

    [ObservableProperty]
    private string _timeOfDay = "02:00";

    [ObservableProperty]
    private bool _sunday;

    [ObservableProperty]
    private bool _monday = true;

    [ObservableProperty]
    private bool _tuesday = true;

    [ObservableProperty]
    private bool _wednesday = true;

    [ObservableProperty]
    private bool _thursday = true;

    [ObservableProperty]
    private bool _friday = true;

    [ObservableProperty]
    private bool _saturday;

    [ObservableProperty]
    private bool _isScheduleEnabled = true;

    [ObservableProperty]
    private bool _runWhetherUserLoggedOnOrNot;

    [ObservableProperty]
    private bool _hasExistingSchedule;

    [ObservableProperty]
    private string? _statusMessage;

    public string JobName => _job.Name;

    public ScheduleEditorViewModel(ITaskSchedulerService taskSchedulerService, ScheduleMetadataRepository scheduleMetadataRepository, Job job)
    {
        _taskSchedulerService = taskSchedulerService;
        _scheduleMetadataRepository = scheduleMetadataRepository;
        _job = job;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _existing = await _scheduleMetadataRepository.GetByJobIdAsync(_job.Id);
        HasExistingSchedule = _existing is not null;
        if (_existing is not null)
        {
            IsScheduleEnabled = _existing.IsEnabled;
            RunWhetherUserLoggedOnOrNot = _existing.RunWhetherUserLoggedOnOrNot;

            var liveInfo = _taskSchedulerService.GetLiveTaskInfo(_existing.TaskSchedulerTaskName);
            StatusMessage = liveInfo is null
                ? "This schedule's task no longer exists in Task Scheduler — saving will recreate it."
                : $"Current OS task trigger: {liveInfo.TriggerDescription}";
        }
    }

    /// <returns>true if saved successfully and the dialog should close.</returns>
    public async Task<bool> SaveAsync(string? windowsAccountPassword)
    {
        if (!TimeSpan.TryParse(TimeOfDay, out var timeOfDay))
        {
            MessageBox.Show("Time must be in HH:mm format (24-hour).", "Invalid time", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var days = new List<DayOfWeek>();
        if (Sunday) days.Add(DayOfWeek.Sunday);
        if (Monday) days.Add(DayOfWeek.Monday);
        if (Tuesday) days.Add(DayOfWeek.Tuesday);
        if (Wednesday) days.Add(DayOfWeek.Wednesday);
        if (Thursday) days.Add(DayOfWeek.Thursday);
        if (Friday) days.Add(DayOfWeek.Friday);
        if (Saturday) days.Add(DayOfWeek.Saturday);

        if (Frequency == ScheduleFrequency.Weekly && days.Count == 0)
        {
            MessageBox.Show("Select at least one day for a weekly schedule.", "Missing days", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (RunWhetherUserLoggedOnOrNot && string.IsNullOrEmpty(windowsAccountPassword))
        {
            MessageBox.Show(
                "Running whether logged on or not requires your Windows account password (stored by Windows itself, not this app).",
                "Missing password", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        var taskName = _existing?.TaskSchedulerTaskName ?? $"Job_{_job.Id}";
        var trigger = new ScheduleTriggerInfo(Frequency, timeOfDay, days);

        try
        {
            _taskSchedulerService.RegisterOrUpdateTask(taskName, _job.Id, trigger, IsScheduleEnabled, RunWhetherUserLoggedOnOrNot, windowsAccountPassword);
        }
        catch (Exception ex)
        {
            // RegisterTaskDefinition is a COM call into Task Scheduler -- a wrong Windows
            // account password or an access-denied condition throws here, and with no
            // DispatcherUnhandledException handler anywhere in the app, letting this escape
            // Save_Click's async void would crash the whole application.
            MessageBox.Show($"Couldn't save the schedule: {ex.Message}", "Schedule save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }

        var liveInfo = _taskSchedulerService.GetLiveTaskInfo(taskName);

        var metadata = _existing ?? new ScheduleMetadata { JobId = _job.Id, TaskSchedulerTaskName = taskName };
        metadata.TriggerDescription = liveInfo?.TriggerDescription;
        metadata.IsEnabled = IsScheduleEnabled;
        metadata.RunWhetherUserLoggedOnOrNot = RunWhetherUserLoggedOnOrNot;
        metadata.LastSyncedUtc = DateTime.UtcNow;

        if (_existing is null)
        {
            metadata.Id = await _scheduleMetadataRepository.InsertAsync(metadata);
        }
        else
        {
            await _scheduleMetadataRepository.UpdateAsync(metadata);
        }

        return true;
    }

    public async Task RemoveScheduleAsync()
    {
        if (_existing is null)
        {
            return;
        }

        _taskSchedulerService.DeleteTask(_existing.TaskSchedulerTaskName);
        await _scheduleMetadataRepository.DeleteAsync(_existing.Id);
    }
}
