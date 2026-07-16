using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using WindowsBackupHelper.Core.Credentials;

namespace WindowsBackupHelper.Win.Credentials;

/// <summary>
/// ICredentialStore backed by the real Windows Credential Manager vault via advapi32.dll's
/// CredRead/CredWrite/CredDelete/CredFree, generic credential type,
/// CRED_PERSIST_LOCAL_MACHINE (persists across reboots, available to scheduled-task runs,
/// scoped to the Windows account that created it — not machine-wide across all users).
/// </summary>
public sealed class WindowsCredentialStore : ICredentialStore
{
    public void Save(string targetName, string userName, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(userName);
        ArgumentNullException.ThrowIfNull(password);

        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var blobPtr = Marshal.AllocHGlobal(passwordBytes.Length);
        try
        {
            Marshal.Copy(passwordBytes, 0, blobPtr, passwordBytes.Length);

            var credential = new Credential
            {
                Flags = 0,
                Type = CredentialNativeConstants.CredTypeGeneric,
                TargetName = targetName,
                CredentialBlobSize = (uint)passwordBytes.Length,
                CredentialBlob = blobPtr,
                Persist = CredentialNativeConstants.CredPersistLocalMachine,
                UserName = userName,
            };

            if (!NativeCredentialMethods.CredWrite(ref credential, 0))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"Failed to save credential '{targetName}' to Windows Credential Manager.");
            }
        }
        finally
        {
            // Zero the buffer before freeing it — this held a plaintext password.
            Marshal.Copy(new byte[passwordBytes.Length], 0, blobPtr, passwordBytes.Length);
            Marshal.FreeHGlobal(blobPtr);
        }
    }

    public StoredCredential? TryRead(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        if (!NativeCredentialMethods.CredRead(targetName, CredentialNativeConstants.CredTypeGeneric, 0, out var credentialPtr))
        {
            var error = Marshal.GetLastWin32Error();
            const int ErrorNotFound = 1168;
            if (error == ErrorNotFound)
            {
                return null;
            }

            throw new Win32Exception(error, $"Failed to read credential '{targetName}' from Windows Credential Manager.");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
            var password = string.Empty;
            if (credential.CredentialBlobSize > 0)
            {
                var passwordBytes = new byte[credential.CredentialBlobSize];
                Marshal.Copy(credential.CredentialBlob, passwordBytes, 0, passwordBytes.Length);
                password = Encoding.Unicode.GetString(passwordBytes);
            }

            return new StoredCredential(credential.UserName ?? string.Empty, password);
        }
        finally
        {
            NativeCredentialMethods.CredFree(credentialPtr);
        }
    }

    public void Delete(string targetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);

        if (!NativeCredentialMethods.CredDelete(targetName, CredentialNativeConstants.CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            const int ErrorNotFound = 1168;
            if (error == ErrorNotFound)
            {
                return;
            }

            throw new Win32Exception(error, $"Failed to delete credential '{targetName}' from Windows Credential Manager.");
        }
    }
}
