using System.Diagnostics;
using System.Security.Principal;
using WindowsBackupHelper.Core.Elevation;

namespace WindowsBackupHelper.Win.Elevation;

/// <summary>
/// Detects and requests admin elevation, needed for backup-mode Robocopy runs (/B, /ZB).
/// </summary>
public sealed class ElevationService : IElevationService
{
    public bool IsRunningElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public void RelaunchElevated(IReadOnlyList<string> arguments)
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine the current process's executable path.");

        var startInfo = new ProcessStartInfo(exePath)
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        Process.Start(startInfo);
    }
}
