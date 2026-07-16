using WindowsBackupHelper.Core.Checksums;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Scheduling;

namespace WindowsBackupHelper.App.Views;

/// <summary>Enum value lists for XAML ItemsSource bindings (x:Static), since XAML can't call Enum.GetValues directly.</summary>
public static class EnumValueSource
{
    public static ExclusionPatternType[] PatternTypes { get; } = Enum.GetValues<ExclusionPatternType>();

    public static ExclusionTargetType[] TargetTypes { get; } = Enum.GetValues<ExclusionTargetType>();

    public static ExclusionScope[] Scopes { get; } = Enum.GetValues<ExclusionScope>();

    public static ChecksumMode[] ChecksumModes { get; } = Enum.GetValues<ChecksumMode>();

    public static RunTriggerType[] TriggerTypes { get; } = Enum.GetValues<RunTriggerType>();

    public static ScheduleFrequency[] Frequencies { get; } = Enum.GetValues<ScheduleFrequency>();
}
