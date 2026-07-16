using WindowsBackupHelper.Core.Checksums;

namespace WindowsBackupHelper.Core.Tests.Checksums;

/// <summary>A controllable IFileHasher keyed by absolute path, for testing checksum services without real hashing.</summary>
public sealed class FakeFileHasher(
    IReadOnlyDictionary<string, string>? digestsByAbsolutePath = null,
    IEnumerable<string>? throwsForAbsolutePath = null) : IFileHasher
{
    private readonly IReadOnlyDictionary<string, string> _digests = digestsByAbsolutePath ?? new Dictionary<string, string>();
    private readonly HashSet<string> _throwsFor = throwsForAbsolutePath?.ToHashSet() ?? [];

    public Task<string> ComputeSha256Async(string absolutePath, CancellationToken cancellationToken = default)
    {
        if (_throwsFor.Contains(absolutePath))
        {
            throw new IOException($"Simulated read error for '{absolutePath}'.");
        }

        return Task.FromResult(_digests.TryGetValue(absolutePath, out var digest) ? digest : "deadbeef");
    }
}
