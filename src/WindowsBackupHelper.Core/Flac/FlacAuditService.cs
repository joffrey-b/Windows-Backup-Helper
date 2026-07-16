using System.Collections.Concurrent;
using WindowsBackupHelper.Core.Exclusions;

namespace WindowsBackupHelper.Core.Flac;

/// <summary>
/// Ports flac_audit_windows_linux.py's discovery + concurrent verification loop: finds all
/// *.flac files recursively, runs the classifier over each (bounded parallelism, since
/// flac -t is I/O-bound over network shares), and sorts results errors-then-warnings-then-ok
/// then by path — matching the Python script's report ordering.
/// </summary>
public sealed class FlacAuditService(IFileSystemEnumerator fileSystemEnumerator, IFlacProcessRunner flacProcessRunner)
{
    public async Task<IReadOnlyList<FlacFileResult>> RunAsync(
        string root, int workers, CancellationToken cancellationToken = default, IProgress<int>? progress = null)
    {
        var flacFiles = fileSystemEnumerator.Enumerate(root)
            .Where(e => !e.IsDirectory && e.RelativePath.EndsWith(".flac", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var results = new ConcurrentBag<FlacFileResult>();
        var completed = 0;

        await Parallel.ForEachAsync(
            flacFiles,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, workers), CancellationToken = cancellationToken },
            async (relativePath, ct) =>
            {
                var absolutePath = ToAbsolutePath(root, relativePath);
                var processResult = await flacProcessRunner.RunAsync(absolutePath, ct).ConfigureAwait(false);
                var classification = FlacResultClassifier.Classify(processResult, absolutePath);
                results.Add(new FlacFileResult(relativePath, classification.Status, classification.Messages));
                progress?.Report(Interlocked.Increment(ref completed));
            }).ConfigureAwait(false);

        return SortForReport(results);
    }

    internal static IReadOnlyList<FlacFileResult> SortForReport(IEnumerable<FlacFileResult> results)
    {
        var statusOrder = new Dictionary<FlacFileStatus, int>
        {
            [FlacFileStatus.Error] = 0,
            [FlacFileStatus.Warning] = 1,
            [FlacFileStatus.Ok] = 2,
        };

        return results
            .OrderBy(r => statusOrder[r.Status])
            .ThenBy(r => r.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private static string ToAbsolutePath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
}
