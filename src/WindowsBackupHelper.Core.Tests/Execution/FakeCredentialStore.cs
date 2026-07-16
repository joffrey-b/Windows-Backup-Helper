using WindowsBackupHelper.Core.Credentials;

namespace WindowsBackupHelper.Core.Tests.Execution;

public sealed class FakeCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, StoredCredential> _credentials = [];

    public void Seed(string targetName, string userName, string password) => _credentials[targetName] = new StoredCredential(userName, password);

    public void Save(string targetName, string userName, string password) => _credentials[targetName] = new StoredCredential(userName, password);

    public StoredCredential? TryRead(string targetName) => _credentials.GetValueOrDefault(targetName);

    public void Delete(string targetName) => _credentials.Remove(targetName);
}
