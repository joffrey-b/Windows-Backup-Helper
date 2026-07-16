using System.Diagnostics;
using System.Text;
using WindowsBackupHelper.Core.Flac;

namespace WindowsBackupHelper.Win.Flac;

/// <summary>
/// Spawns `flac -t --silent &lt;path&gt;` for real via ProcessStartInfo.ArgumentList. A missing or
/// unstartable flac executable surfaces as FlacExecutableNotFoundException, which the UI
/// turns into a pointer at AppSettings.FlacExecutablePath.
/// </summary>
public sealed class FlacProcessRunner(Func<string> resolveFlacExecutablePath) : IFlacProcessRunner
{
    public async Task<FlacProcessResult> RunAsync(string absoluteFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absoluteFilePath);

        var flacExecutablePath = resolveFlacExecutablePath();

        var startInfo = new ProcessStartInfo(flacExecutablePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-t");
        startInfo.ArgumentList.Add("--silent");
        startInfo.ArgumentList.Add(absoluteFilePath);

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outputLock = new Lock();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (outputLock)
                {
                    stdout.AppendLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (outputLock)
                {
                    stderr.AppendLine(e.Data);
                }
            }
        };

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new FlacExecutableNotFoundException(flacExecutablePath, ex);
        }
        catch (FileNotFoundException ex)
        {
            throw new FlacExecutableNotFoundException(flacExecutablePath, ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return new FlacProcessResult(process.ExitCode, stdout.ToString(), stderr.ToString());
    }
}
