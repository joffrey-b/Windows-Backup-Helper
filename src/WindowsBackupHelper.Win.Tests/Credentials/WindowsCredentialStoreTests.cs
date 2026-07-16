using WindowsBackupHelper.Win.Credentials;

namespace WindowsBackupHelper.Win.Tests.Credentials;

/// <summary>
/// Exercises the real Windows Credential Manager vault on this machine (not a fake) — there
/// is no safe way to unit test CredWrite/CredRead/CredDelete without touching the real OS API.
/// Every test uses an obviously test-scoped target name and cleans up in a finally block.
/// </summary>
public sealed class WindowsCredentialStoreTests
{
    private static string NewTestTargetName() => $"WindowsBackupHelperTests:{Guid.NewGuid():N}";

    [Fact]
    public void SaveThenTryRead_RoundTripsUserNameAndPassword()
    {
        var store = new WindowsCredentialStore();
        var target = NewTestTargetName();

        store.Save(target, "nas-user", "correct horse battery staple");
        try
        {
            var read = store.TryRead(target);

            Assert.NotNull(read);
            Assert.Equal("nas-user", read!.UserName);
            Assert.Equal("correct horse battery staple", read.Password);
        }
        finally
        {
            store.Delete(target);
        }
    }

    [Fact]
    public void TryRead_NonexistentTarget_ReturnsNull()
    {
        var store = new WindowsCredentialStore();

        var read = store.TryRead(NewTestTargetName());

        Assert.Null(read);
    }

    [Fact]
    public void Save_Overwrite_ReplacesThePreviousPassword()
    {
        var store = new WindowsCredentialStore();
        var target = NewTestTargetName();

        store.Save(target, "user", "first-password");
        try
        {
            store.Save(target, "user", "second-password");
            var read = store.TryRead(target);

            Assert.Equal("second-password", read!.Password);
        }
        finally
        {
            store.Delete(target);
        }
    }

    [Fact]
    public void Delete_NonexistentTarget_DoesNotThrow()
    {
        var store = new WindowsCredentialStore();

        var exception = Record.Exception(() => store.Delete(NewTestTargetName()));

        Assert.Null(exception);
    }

    [Fact]
    public void Delete_RemovesTheCredential_SoASubsequentReadReturnsNull()
    {
        var store = new WindowsCredentialStore();
        var target = NewTestTargetName();
        store.Save(target, "user", "password");

        store.Delete(target);

        Assert.Null(store.TryRead(target));
    }

    [Fact]
    public void Save_PasswordContainingUnicodeCharacters_RoundTrips()
    {
        // Real-world music library / NAS account names commonly include non-ASCII characters.
        var store = new WindowsCredentialStore();
        var target = NewTestTargetName();
        const string password = "пароль-密码-🔒";

        store.Save(target, "user", password);
        try
        {
            Assert.Equal(password, store.TryRead(target)!.Password);
        }
        finally
        {
            store.Delete(target);
        }
    }
}
