using System.ComponentModel;
using WindowsBackupHelper.Win.Smb;

namespace WindowsBackupHelper.Win.Tests.Smb;

/// <summary>
/// A real, reachable SMB server isn't available to an automated test, so this only exercises
/// the real WNetAddConnection2 failure path (a nonexistent host) — still meaningful, since it
/// proves the P/Invoke marshaling and error surfacing work against the real Win32 API.
/// Connecting to an actual NAS (and the two-credentials-same-host question from the handoff
/// doc) needs manual validation against real hardware.
/// </summary>
public sealed class WNetSmbConnectionManagerTests
{
    [Fact]
    public void Connect_UnreachableHost_ThrowsWin32Exception()
    {
        var manager = new WNetSmbConnectionManager();

        var exception = Assert.Throws<Win32Exception>(
            () => manager.Connect(@"\\wbh-test-host-that-does-not-exist\share", "user", "password"));

        Assert.Contains("Failed to connect", exception.Message);
    }
}
