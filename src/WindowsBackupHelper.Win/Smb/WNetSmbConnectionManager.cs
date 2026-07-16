using System.ComponentModel;
using WindowsBackupHelper.Core.Smb;

namespace WindowsBackupHelper.Win.Smb;

/// <summary>
/// ISmbConnectionManager backed by WNetAddConnection2/WNetCancelConnection2 (mpr.dll) —
/// explicitly not `net.exe use ... /user:x password`, since a plaintext password on a
/// spawned process's command line is visible to Task Manager's command-line column, WMI,
/// or Process Explorer.
/// </summary>
public sealed class WNetSmbConnectionManager : ISmbConnectionManager
{
    public IDisposable Connect(string uncPath, string userName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uncPath);

        var netResource = new NetResource { ResourceType = MprNativeConstants.ResourceTypeDisk, RemoteName = uncPath };

        var result = NativeMprMethods.WNetAddConnection2(
            ref netResource,
            string.IsNullOrEmpty(password) ? null : password,
            string.IsNullOrEmpty(userName) ? null : userName,
            0);

        if (result != MprNativeConstants.NoError)
        {
            throw new Win32Exception(result, $"Failed to connect to '{uncPath}' (WNetAddConnection2 error {result}).");
        }

        return new SmbConnectionHandle(uncPath);
    }

    private sealed class SmbConnectionHandle(string uncPath) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            NativeMprMethods.WNetCancelConnection2(uncPath, 0, true);
            _disposed = true;
        }
    }
}
