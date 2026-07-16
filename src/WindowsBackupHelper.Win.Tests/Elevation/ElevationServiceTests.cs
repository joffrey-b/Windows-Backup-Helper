using WindowsBackupHelper.Win.Elevation;

namespace WindowsBackupHelper.Win.Tests.Elevation;

public sealed class ElevationServiceTests
{
    [Fact]
    public void IsRunningElevated_DoesNotThrow_AndReturnsAConsistentValue()
    {
        // Whether the test host itself runs elevated depends on the machine/CI runner, so this
        // only asserts the real WindowsIdentity/WindowsPrincipal check succeeds and is stable
        // across repeated calls — not a specific true/false value. RelaunchElevated triggers a
        // real UAC prompt and can't be exercised by an automated test.
        var service = new ElevationService();

        var first = service.IsRunningElevated;
        var second = service.IsRunningElevated;

        Assert.Equal(first, second);
    }
}
