using System.Collections.Concurrent;
using WindowsBackupHelper.Core.Exclusions;

namespace WindowsBackupHelper.Core.Checksums;

public sealed record ChecksumUpdateResult(
    IReadOnlyDictionary<string, string> UpdatedEntries,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<(string RelativePath, string Error)> Errors);

/// <summary>
/// Ports checksums_windows_linux.py's "update": hashes only new files (set difference
/// against the existing manifest), drops deleted entries, and never re-hashes anything
/// already known — re-hashing a huge library over SMB is slow, so this incremental
/// behavior matters.
/// </summary>
public sealed class ChecksumUpdateService(IFileSystemEnumerator fileSystemEnumerator, IFileHasher fileHasher)
{
    public async Task<ChecksumUpdateResult> UpdateAsync(
        string root, IReadOnlyDictionary<string, string> manifest, int workers = 4,
        CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        var diskFiles = ChecksumFileDiscovery.FindAllFiles(fileSystemEnumerator, root);
        var diskSet = diskFiles.ToHashSet(StringComparer.Ordinal);

        var newFiles = diskFiles.Where(f => !manifest.ContainsKey(f)).ToList();
        var deletedFiles = manifest.Keys.Where(k => !diskSet.Contains(k)).ToList();

        var updated = new ConcurrentDictionary<string, string>(manifest);
        var errors = new ConcurrentBag<(string, string)>();
        var completed = 0;

        await Parallel.ForEachAsync(
            newFiles,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, workers), CancellationToken = cancellationToken },
            async (relativePath, ct) =>
            {
                try
                {
                    var digest = await fileHasher
                        .ComputeSha256Async(ChecksumFileDiscovery.ToAbsolutePath(root, relativePath), ct)
                        .ConfigureAwait(false);
                    updated[relativePath] = digest;
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

        foreach (var deleted in deletedFiles)
        {
            updated.TryRemove(deleted, out _);
        }

        return new ChecksumUpdateResult(updated, newFiles, deletedFiles, [.. errors]);
    }
}
