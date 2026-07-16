using WindowsBackupHelper.Core.Robocopy;

namespace WindowsBackupHelper.Core.Tests.Execution;

public sealed class FakeRobocopyProcessRunner(Func<IReadOnlyList<string>, RobocopyProcessResult>? resultFactory = null) : IRobocopyProcessRunner
{
    public List<IReadOnlyList<string>> Invocations { get; } = [];

    public Task<RobocopyProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        Invocations.Add(arguments);
        var result = resultFactory?.Invoke(arguments) ?? new RobocopyProcessResult(0, "");
        return Task.FromResult(result);
    }
}
