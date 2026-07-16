using System.Windows.Forms;
using WindowsBackupHelper.Core.Notifications;

namespace WindowsBackupHelper.App.Services;

/// <summary>
/// v1 notification mechanism: System.Windows.Forms.NotifyIcon balloon tips. Zero
/// AUMID/shortcut/COM-activator plumbing regardless of where the exe is launched from
/// (including as a portable single-file exe); still surfaces through the modern Action
/// Center in practice, just without rich interactive buttons. Full interactive toasts are a
/// reasonable v2 enhancement once the core engine is proven.
/// </summary>
public sealed class NotifyIconNotificationService : INotificationService, IDisposable
{
    private readonly NotifyIcon _notifyIcon;

    public NotifyIconNotificationService()
    {
        _notifyIcon = new NotifyIcon
        {
            Icon = LoadAppIcon(),
            Text = "Windows Backup Helper",
            Visible = false,
        };
    }

    /// <summary>
    /// Reads Assets/icon.ico from the compiled-in WPF resource stream — the same one
    /// MainWindow's Icon pack URI points at — rather than the exe/dll path on disk, since
    /// which file that is (and whether it even has a physical path) differs between
    /// `dotnet run`, a normal build, and the self-contained single-file publish.
    /// </summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        var resourceInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico"));
        if (resourceInfo is null)
        {
            return System.Drawing.SystemIcons.Application;
        }

        using var stream = resourceInfo.Stream;
        return new System.Drawing.Icon(stream);
    }

    public void NotifyJobCompleted(string jobName, bool success, string summary)
    {
        _notifyIcon.Visible = true;
        _notifyIcon.BalloonTipTitle = success ? $"'{jobName}' completed" : $"'{jobName}' finished with problems";
        _notifyIcon.BalloonTipText = summary;
        _notifyIcon.BalloonTipIcon = success ? ToolTipIcon.Info : ToolTipIcon.Warning;
        _notifyIcon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
