using System.Collections.Concurrent;
using WindowsBackupHelper.Core.Exclusions;

namespace WindowsBackupHelper.Core.Checksums;

public sealed record ChecksumGenerateResult(
    IReadOnlyDictionary<string, string> Entries,
    IReadOnlyList<(string RelativePath, string Error)> Errors);

/// <summary>Ports checksums_windows_linux.py's "generate": hash every discovered file, no baseline to compare against.</summary>
public sealed class ChecksumGenerateService(IFileSystemEnumerator fileSystemEnumerator, IFileHasher fileHasher)
{
    public async Task<ChecksumGenerateResult> GenerateAsync(
        string root, int workers = 4, CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        var files = ChecksumFileDiscovery.FindAllFiles(fileSystemEnumerator, root);
        var entries = new ConcurrentDictionary<string, string>();
        var errors = new ConcurrentBag<(string, string)>();
        var completed = 0;

        await Parallel.ForEachAsync(
            files,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, workers), CancellationToken = cancellationToken },
            async (relativePath, ct) =>
            {
                try
                {
                    var digest = await fileHasher
                        .ComputeSha256Async(ChecksumFileDiscovery.ToAbsolutePath(root, relativePath), ct)
                        .ConfigureAwait(false);
                    entries[relativePath] = digest;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    errors.Add((relativePath, ex.Message));
                }
                finally
                {
                    progress?.Report(Interlocked.Increment(ref completed));
                }
            }).ConfigureAwait(false);

        return new ChecksumGenerateResult(entries, [.. errors]);
    }
}
