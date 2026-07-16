using System.Collections.Concurrent;

namespace WindowsBackupHelper.Core.Checksums;

public sealed record ChecksumVerifyResult(
    IReadOnlyList<string> Ok,
    IReadOnlyList<string> Changed,
    IReadOnlyList<string> Missing,
    IReadOnlyList<(string RelativePath, string Error)> ReadErrors);

/// <summary>
/// Ports checksums_windows_linux.py's "verify": re-hashes every file the manifest
/// references that still exists on disk. Missing entries are partitioned out before
/// hashing and never attempted — matching the Python script exactly. Files present on
/// disk but absent from the manifest are not reported, also matching the Python script.
/// </summary>
public sealed class ChecksumVerifyService(IFileHasher fileHasher)
{
    public async Task<ChecksumVerifyResult> VerifyAsync(
        string root, IReadOnlyDictionary<string, string> manifest, int workers = 4,
        CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        var toCheck = new List<string>();
        var missing = new List<string>();

        foreach (var relativePath in manifest.Keys)
        {
            if (File.Exists(ChecksumFileDiscovery.ToAbsolutePath(root, relativePath)))
            {
                toCheck.Add(relativePath);
            }
            else
            {
                missing.Add(relativePath);
            }
        }

        var ok = new ConcurrentBag<string>();
        var changed = new ConcurrentBag<string>();
        var readErrors = new ConcurrentBag<(string, string)>();
        var completed = 0;

        await Parallel.ForEachAsync(
            toCheck,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, workers), CancellationToken = cancellationToken },
            async (relativePath, ct) =>
            {
                try
                {
                    var digest = await fileHasher
                        .ComputeSha256Async(ChecksumFileDiscovery.ToAbsolutePath(root, relativePath), ct)
                        .ConfigureAwait(false);
                    if (string.Equals(digest, manifest[relativePath], StringComparison.OrdinalIgnoreCase))
                    {
                        ok.Add(relativePath);
                    }
                    else
                    {
                        changed.Add(relativePath);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    readErrors.Add((relativePath, ex.Message));
                }
                finally
                {
                    progress?.Report(Interlocked.Increment(ref completed));
                }
            }).ConfigureAwait(false);

        return new ChecksumVerifyResult(
            ok.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            changed.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            missing.OrderBy(x => x, StringComparer.Ordinal).ToList(),
            readErrors.OrderBy(x => x.Item1, StringComparer.Ordinal).ToList());
    }
}
