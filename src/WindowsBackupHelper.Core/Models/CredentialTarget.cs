namespace WindowsBackupHelper.Core.Models;

/// <summary>
/// A pointer into Windows Credential Manager. Deliberately has no username/password
/// columns — CredRead returns both at connect-time. There is structurally no column
/// here capable of holding a secret.
/// </summary>
public sealed class CredentialTarget
{
    public required string Id { get; init; }
    public required string Label { get; set; }
    public required string HostOrUncRoot { get; set; }

    /// <summary>e.g. "WindowsBackupHelper:{Id}" — GUID-keyed so renaming a NAS label never orphans the secret.</summary>
    public required string CredentialManagerTargetName { get; set; }
}
