namespace WindowsBackupHelper.Core.Tests;

/// <summary>
/// A synchronous IProgress&lt;T&gt; test double: System.Progress&lt;T&gt; marshals Report() calls
/// through a captured SynchronizationContext (falling back to the ThreadPool when there is
/// none), which makes its callback timing non-deterministic in a test that has no
/// SynchronizationContext of its own. This records reports directly on the calling thread.
///
/// The services under test report via Parallel.ForEachAsync, so Report() can genuinely be
/// called from multiple threads at once -- List&lt;T&gt;.Add is not thread-safe, so without the
/// lock, concurrent reports can silently overwrite each other and drop a value.
/// </summary>
public sealed class RecordingProgress<T> : IProgress<T>
{
    private readonly Lock _lock = new();

    public List<T> Reports { get; } = [];

    public void Report(T value)
    {
        lock (_lock)
        {
            Reports.Add(value);
        }
    }
}
