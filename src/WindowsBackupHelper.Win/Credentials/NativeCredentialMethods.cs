using System.Runtime.InteropServices;

namespace WindowsBackupHelper.Win.Credentials;

[StructLayout(LayoutKind.Sequential)]
internal struct FileTime
{
    public uint LowDateTime;
    public uint HighDateTime;
}

// Mirrors the Win32 CREDENTIALW struct (wincred.h) field-for-field. Kept as classic
// DllImport/CharSet.Unicode marshaling rather than the source-generated LibraryImport:
// LibraryImport's marshaller does not reliably support structs containing embedded
// `string` fields passed by ref, which this struct requires.
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct Credential
{
    public uint Flags;
    public uint Type;
    public string TargetName;
    public string? Comment;
    public FileTime LastWritten;
    public uint CredentialBlobSize;
    public IntPtr CredentialBlob;
    public uint Persist;
    public uint AttributeCount;
    public IntPtr Attributes;
    public string? TargetAlias;
    public string? UserName;
}

internal static class CredentialNativeConstants
{
    public const uint CredTypeGeneric = 1;
    public const uint CredPersistLocalMachine = 2;
}

internal static class NativeCredentialMethods
{
    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredRead(string target, uint type, uint flags, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    public static extern void CredFree(IntPtr credential);
}
