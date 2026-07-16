namespace WindowsBackupHelper.Core.Smb;

/// <summary>
/// Abstracts establishing an SMB session to a UNC path. The real implementation uses
/// WNetAddConnection2 — explicitly not `net.exe use ... /user:x password`, since a plaintext
/// password on a spawned process's command line is visible to any concurrent
/// process-listing tool. Connections should be acquired immediately before a folder pair's
/// Robocopy invocation and disposed (WNetCancelConnection2) immediately after, in a
/// try/finally, to minimize interference with the user's own manual `net use` mappings and
/// limit blast radius if the app crashes mid-run.
/// </summary>
public interface ISmbConnectionManager
{
    IDisposable Connect(string uncPath, string userName, string password);
}
