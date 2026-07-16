using System.Runtime.InteropServices;

namespace WindowsBackupHelper.Win.Smb;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct NetResource
{
    public int Scope;
    public int ResourceType;
    public int DisplayType;
    public int Usage;
    public string? LocalName;
    public string RemoteName;
    public string? Comment;
    public string? Provider;
}

internal static class MprNativeConstants
{
    public const int ResourceTypeDisk = 1;
    public const int NoError = 0;
}

internal static class NativeMprMethods
{
    [DllImport("mpr.dll", EntryPoint = "WNetAddConnection2W", CharSet = CharSet.Unicode)]
    public static extern int WNetAddConnection2(ref NetResource netResource, string? password, string? userName, int flags);

    [DllImport("mpr.dll", EntryPoint = "WNetCancelConnection2W", CharSet = CharSet.Unicode)]
    public static extern int WNetCancelConnection2(string name, int flags, [MarshalAs(UnmanagedType.Bool)] bool force);
}
