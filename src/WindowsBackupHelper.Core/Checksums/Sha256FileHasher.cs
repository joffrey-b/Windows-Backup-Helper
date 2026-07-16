using System.Security.Cryptography;

namespace WindowsBackupHelper.Core.Checksums;

/// <summary>
/// Real SHA256 hashing, 1MB chunked reads — memory-friendly for large files read over a NAS,
/// matching checksums_windows_linux.py's own chunking. Has no Windows dependency at all
/// (System.IO/System.Security.Cryptography only), so it lives in Core.
/// </summary>
public sealed class Sha256FileHasher : IFileHasher
{
    private const int ChunkSizeBytes = 1024 * 1024;

    public async Task<string> ComputeSha256Async(string absolutePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSizeBytes, useAsync: true);
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var buffer = new byte[ChunkSizeBytes];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, ChunkSizeBytes), cancellationToken).ConfigureAwait(false)) > 0)
        {
            incrementalHash.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
    }
}
