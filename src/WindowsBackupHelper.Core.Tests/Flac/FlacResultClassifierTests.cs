using WindowsBackupHelper.Core.Flac;

namespace WindowsBackupHelper.Core.Tests.Flac;

public sealed class FlacResultClassifierTests
{
    private const string FilePath = @"D:\Music\Artist\Album\track.flac";

    [Fact]
    public void Classify_ZeroExitNoWarningText_IsOk()
    {
        var result = FlacResultClassifier.Classify(new FlacProcessResult(0, "", ""), FilePath);

        Assert.Equal(FlacFileStatus.Ok, result.Status);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Classify_NonZeroExit_IsError_AndFiltersOutTheEchoedFilePathLine()
    {
        // Real `flac -t` prints the file path itself as a progress line before any error detail.
        var combinedOutput = $"{FilePath} : *** Got error code 1:MEMORY ALLOCATION ERROR ***\nERROR: while decoding data";
        var result = FlacResultClassifier.Classify(new FlacProcessResult(1, "", combinedOutput), FilePath);

        Assert.Equal(FlacFileStatus.Error, result.Status);
        Assert.Contains(result.Messages, m => m.Contains("ERROR: while decoding data"));
        Assert.DoesNotContain(result.Messages, m => m.StartsWith(FilePath, StringComparison.Ordinal));
    }

    [Fact]
    public void Classify_NonZeroExit_NoUsableMessageLines_FallsBackToUnknownError()
    {
        // Every line starts with the file path, so all lines get filtered out.
        var result = FlacResultClassifier.Classify(new FlacProcessResult(1, "", $"{FilePath}: some detail"), FilePath);

        Assert.Equal(FlacFileStatus.Error, result.Status);
        Assert.Equal(["Unknown error"], result.Messages);
    }

    [Fact]
    public void Classify_ZeroExitWithCannotCheckMd5_IsWarning()
    {
        var result = FlacResultClassifier.Classify(
            new FlacProcessResult(0, $"{FilePath}: ok, cannot check MD5 signature", ""), FilePath);

        Assert.Equal(FlacFileStatus.Warning, result.Status);
        Assert.Contains(result.Messages, m => m.Contains("cannot check"));
    }

    [Fact]
    public void Classify_ZeroExitWithWarningText_IsWarning()
    {
        var result = FlacResultClassifier.Classify(
            new FlacProcessResult(0, "", "WARNING, generic warning message"), FilePath);

        Assert.Equal(FlacFileStatus.Warning, result.Status);
        Assert.Contains(result.Messages, m => m.Contains("WARNING"));
    }

    [Fact]
    public void Classify_ZeroExitWithWarningText_ExtractsOnlyTheMatchingLines()
    {
        var result = FlacResultClassifier.Classify(
            new FlacProcessResult(0, "some unrelated ok line\nWARNING: no MD5 sum", ""), FilePath);

        Assert.Equal(FlacFileStatus.Warning, result.Status);
        Assert.Equal(["WARNING: no MD5 sum"], result.Messages);
    }

    [Fact]
    public void Classify_IsPureAndDeterministic_SameInputSameOutput()
    {
        // Assert.Equal on the records themselves would fall back to reference equality for the
        // List<string> Messages property, so compare fields (which xUnit compares by sequence).
        var input = new FlacProcessResult(1, "stdout line", "stderr line");
        var first = FlacResultClassifier.Classify(input, FilePath);
        var second = FlacResultClassifier.Classify(input, FilePath);

        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.Messages, second.Messages);
    }
}
