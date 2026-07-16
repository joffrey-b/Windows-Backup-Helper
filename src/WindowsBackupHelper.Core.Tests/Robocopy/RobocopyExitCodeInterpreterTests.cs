using WindowsBackupHelper.Core.Robocopy;

namespace WindowsBackupHelper.Core.Tests.Robocopy;

public sealed class RobocopyExitCodeInterpreterTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void Interpret_ExitCodesBelow8_AreSuccess(int exitCode)
    {
        var outcome = RobocopyExitCodeInterpreter.Interpret(exitCode);
        Assert.True(outcome.IsSuccess);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(24)]
    public void Interpret_ExitCodesAtOrAbove8_AreFailure(int exitCode)
    {
        var outcome = RobocopyExitCodeInterpreter.Interpret(exitCode);
        Assert.False(outcome.IsSuccess);
    }

    [Fact]
    public void Interpret_ZeroExitCode_MeansNoChangeNeeded()
    {
        var outcome = RobocopyExitCodeInterpreter.Interpret(0);

        Assert.Equal(RobocopySeverity.NoChange, outcome.Severity);
        Assert.Equal(RobocopyResultFlags.None, outcome.FlagsSet);
    }

    [Fact]
    public void Interpret_ExitCodeOne_MeansFilesCopiedSuccessfully()
    {
        var outcome = RobocopyExitCodeInterpreter.Interpret(1);

        Assert.Equal(RobocopySeverity.Success, outcome.Severity);
        Assert.True(outcome.FlagsSet.HasFlag(RobocopyResultFlags.FilesCopied));
    }

    [Fact]
    public void Interpret_ExitCodeThree_CombinesFilesCopiedAndExtraFiles()
    {
        // 3 = 1 (files copied) + 2 (extra files/dirs)
        var outcome = RobocopyExitCodeInterpreter.Interpret(3);

        Assert.True(outcome.IsSuccess);
        Assert.Equal(RobocopySeverity.SuccessWithExtrasOrMismatches, outcome.Severity);
        Assert.True(outcome.FlagsSet.HasFlag(RobocopyResultFlags.FilesCopied));
        Assert.True(outcome.FlagsSet.HasFlag(RobocopyResultFlags.ExtraFilesOrDirectories));
    }

    [Fact]
    public void Interpret_ExitCodeEight_MeansCopyErrorsOccurred_AndIsAFailure()
    {
        var outcome = RobocopyExitCodeInterpreter.Interpret(8);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(RobocopySeverity.Failure, outcome.Severity);
        Assert.True(outcome.FlagsSet.HasFlag(RobocopyResultFlags.CopyErrorsOccurred));
    }

    [Fact]
    public void Interpret_ExitCodeSixteen_MeansFatalError()
    {
        var outcome = RobocopyExitCodeInterpreter.Interpret(16);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(RobocopySeverity.FatalError, outcome.Severity);
        Assert.True(outcome.FlagsSet.HasFlag(RobocopyResultFlags.SeriousError));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-1073741819)] // 0xC0000005 (access violation) as a signed int
    public void Interpret_NegativeExitCode_IsNotSuccess_AndIsFatalError(int exitCode)
    {
        var outcome = RobocopyExitCodeInterpreter.Interpret(exitCode);

        Assert.False(outcome.IsSuccess);
        Assert.Equal(RobocopySeverity.FatalError, outcome.Severity);
    }

    [Fact]
    public void Interpret_HumanReadableSummary_IsNeverEmpty()
    {
        foreach (var exitCode in new[] { -1, 0, 1, 3, 7, 8, 16, 31 })
        {
            var outcome = RobocopyExitCodeInterpreter.Interpret(exitCode);
            Assert.False(string.IsNullOrWhiteSpace(outcome.HumanReadableSummary));
        }
    }
}
