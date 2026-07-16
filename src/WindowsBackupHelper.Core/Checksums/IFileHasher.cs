namespace WindowsBackupHelper.Core.Checksums;

/// <summary>Abstracts SHA256 hashing so checksum services are unit-testable without real files.</summary>
public interface IFileHasher
{
    Task<string> ComputeSha256Async(string absolutePath, CancellationToken cancellationToken = default);
}
