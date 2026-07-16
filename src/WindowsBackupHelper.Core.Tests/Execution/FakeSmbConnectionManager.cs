using WindowsBackupHelper.Core.Smb;

namespace WindowsBackupHelper.Core.Tests.Execution;

public sealed class FakeSmbConnectionManager : ISmbConnectionManager
{
    public List<(string UncPath, string UserName, string Password)> Connections { get; } = [];

    public List<string> DisposedPaths { get; } = [];

    public IDisposable Connect(string uncPath, string userName, string password)
    {
        Connections.Add((uncPath, userName, password));
        return new DisposableAction(() => DisposedPaths.Add(uncPath));
    }

    private sealed class DisposableAction(Action onDispose) : IDisposable
    {
        public void Dispose() => onDispose();
    }
}
