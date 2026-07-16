using System.Diagnostics;
using System.Text;
using WindowsBackupHelper.Core.Robocopy;

namespace WindowsBackupHelper.Win.Robocopy;

/// <summary>
/// Spawns robocopy.exe for real: RedirectStandardOutput/Error=true with async
/// OutputDataReceived/ErrorDataReceived draining, never synchronous ReadToEnd (which
/// deadlocks once the OS pipe buffer fills while nobody's draining it).
/// </summary>
public sealed class RobocopyProcessRunner(string robocopyExecutablePath = "robocopy.exe") : IRobocopyProcessRunner
{
    public async Task<RobocopyProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(robocopyExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var output = new StringBuilder();
        var outputLock = new Lock();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (outputLock)
                {
                    output.AppendLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (outputLock)
                {
                    output.AppendLine(e.Data);
                }
            }
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new FileNotFoundException($"Could not start Robocopy at '{robocopyExecutablePath}'.", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Killing mid-copy can leave one partially-copied file at the destination; this
            // self-heals on the next run since Robocopy re-compares by default.
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            throw;
        }

        return new RobocopyProcessResult(process.ExitCode, output.ToString());
    }
}
