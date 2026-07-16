using System.Diagnostics;
using WindowsBackupHelper.Core.Flac;
using WindowsBackupHelper.Win.Flac;

namespace WindowsBackupHelper.Win.Tests.Flac;

/// <summary>
/// Exercises a real flac.exe (must be on PATH or otherwise resolvable — the same
/// requirement flac_audit_windows_linux.py itself has) through FlacProcessRunner and
/// FlacResultClassifier together.
/// </summary>
public sealed class FlacProcessRunnerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"wbh-flac-{Guid.NewGuid():N}");

    public FlacProcessRunnerTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public async Task RunAsync_CorruptFile_ClassifiesAsError()
    {
        var fakeFlacPath = Path.Combine(_tempDir, "garbage.flac");
        await File.WriteAllTextAsync(fakeFlacPath, "this is not a real FLAC file");

        var runner = new FlacProcessRunner(() => "flac.exe");
        var processResult = await runner.RunAsync(fakeFlacPath);

        Assert.NotEqual(0, processResult.ExitCode);

        var classification = FlacResultClassifier.Classify(processResult, fakeFlacPath);
        Assert.Equal(FlacFileStatus.Error, classification.Status);
        Assert.NotEmpty(classification.Messages);
    }

    [Fact]
    public async Task RunAsync_RealValidFlacFile_EncodedByFlacItself_ClassifiesAsOk()
    {
        var wavPath = Path.Combine(_tempDir, "tone.wav");
        var flacPath = Path.Combine(_tempDir, "tone.flac");
        await File.WriteAllBytesAsync(wavPath, BuildMinimalPcmWav());

        await EncodeToFlacAsync(wavPath, flacPath);
        Assert.True(File.Exists(flacPath), "flac.exe did not produce an output file — is flac.exe on PATH?");

        var runner = new FlacProcessRunner(() => "flac.exe");
        var processResult = await runner.RunAsync(flacPath);
        var classification = FlacResultClassifier.Classify(processResult, flacPath);

        Assert.Equal(0, processResult.ExitCode);
        Assert.Equal(FlacFileStatus.Ok, classification.Status);
    }

    [Fact]
    public async Task RunAsync_NonexistentFlacExecutable_ThrowsFlacExecutableNotFoundException()
    {
        var runner = new FlacProcessRunner(() => @"C:\does\not\exist\flac.exe");

        await Assert.ThrowsAsync<FlacExecutableNotFoundException>(
            () => runner.RunAsync(Path.Combine(_tempDir, "whatever.flac")));
    }

    private static async Task EncodeToFlacAsync(string wavPath, string flacPath)
    {
        var startInfo = new ProcessStartInfo("flac.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("--totally-silent");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(wavPath);
        startInfo.ArgumentList.Add("-o");
        startInfo.ArgumentList.Add(flacPath);

        using var process = Process.Start(startInfo)!;
        await process.WaitForExitAsync();
    }

    /// <summary>A tiny valid 16-bit mono PCM WAV file (silence) — just enough for flac.exe to encode.</summary>
    private static byte[] BuildMinimalPcmWav()
    {
        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 1;
        const int sampleCount = 4410; // 0.1s of silence

        var dataSize = sampleCount * channels * (bitsPerSample / 8);
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((short)1); // PCM
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataSize);
        writer.Write(new byte[dataSize]); // silence

        return stream.ToArray();
    }
}
