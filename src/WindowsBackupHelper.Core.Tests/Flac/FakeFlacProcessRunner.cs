using WindowsBackupHelper.Core.Flac;

namespace WindowsBackupHelper.Core.Tests.Flac;

/// <summary>A controllable IFlacProcessRunner keyed by absolute path, for testing without a real flac.exe.</summary>
public sealed class FakeFlacProcessRunner(IReadOnlyDictionary<string, FlacProcessResult> resultsByAbsolutePath) : IFlacProcessRunner
{
    public Task<FlacProcessResult> RunAsync(string absoluteFilePath, CancellationToken cancellationToken = default) =>
        Task.FromResult(
            resultsByAbsolutePath.TryGetValue(absoluteFilePath, out var result) ? result : new FlacProcessResult(0, "", ""));
}
