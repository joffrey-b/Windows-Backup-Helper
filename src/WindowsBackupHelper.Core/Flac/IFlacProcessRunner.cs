namespace WindowsBackupHelper.Core.Flac;

/// <summary>
/// Abstracts spawning `flac -t --silent &lt;path&gt;`. The real implementation (Win project)
/// is the one Windows-touching seam of the FLAC audit feature; this interface lets
/// FlacAuditService and FlacResultClassifier be tested without flac.exe installed or real files.
/// </summary>
public interface IFlacProcessRunner
{
    Task<FlacProcessResult> RunAsync(string absoluteFilePath, CancellationToken cancellationToken = default);
}
