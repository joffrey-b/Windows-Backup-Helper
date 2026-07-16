namespace WindowsBackupHelper.Core.Credentials;

public sealed record StoredCredential(string UserName, string Password);

/// <summary>
/// Abstracts the Windows Credential Manager vault. The app's own SQLite database never
/// contains a secret — CredentialTarget rows only store a TargetName pointing in here,
/// resolved at connect-time.
/// </summary>
public interface ICredentialStore
{
    void Save(string targetName, string userName, string password);

    StoredCredential? TryRead(string targetName);

    void Delete(string targetName);
}
