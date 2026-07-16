namespace WindowsBackupHelper.Core.Elevation;

/// <summary>
/// Abstracts admin-elevation detection and re-launch, needed because backup-mode Robocopy
/// runs (/B, /ZB) require the app to be running as Administrator.
/// </summary>
public interface IElevationService
{
    bool IsRunningElevated { get; }

    void RelaunchElevated(IReadOnlyList<string> arguments);
}
