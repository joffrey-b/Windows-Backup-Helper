using Dapper;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Tests.Data;

namespace WindowsBackupHelper.Core.Tests.Repositories;

public sealed class SupportingRepositoriesTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task RobocopyOptionSet_RoundTripsNullableColumnsIncludingFalseAndZero()
    {
        var repo = new RobocopyOptionSetRepository(_db.Connection);
        var optionSet = new RobocopyOptionSet
        {
            Id = Guid.NewGuid().ToString(),
            Mirror = true,
            Purge = false, // must round-trip as false, not be conflated with "unset"
            Retries = 0,   // must round-trip as 0, not be conflated with "unset"
            MultithreadCount = null,
            ExtraRawArguments = "/NFL /NDL",
        };

        await repo.InsertAsync(optionSet);
        var fetched = await repo.GetByIdAsync(optionSet.Id);

        Assert.True(fetched!.Mirror);
        Assert.False(fetched.Purge);
        Assert.Equal(0, fetched.Retries);
        Assert.Null(fetched.MultithreadCount);
        Assert.Equal("/NFL /NDL", fetched.ExtraRawArguments);
    }

    [Fact]
    public async Task AppSettings_Singleton_UpsertRoundTrips()
    {
        var optionSetRepo = new RobocopyOptionSetRepository(_db.Connection);
        var optionSetId = Guid.NewGuid().ToString();
        await optionSetRepo.InsertAsync(new RobocopyOptionSet { Id = optionSetId, Retries = 3, WaitSeconds = 5 });

        var settingsRepo = new AppSettingsRepository(_db.Connection);
        await settingsRepo.InsertAsync(new AppSettings { DefaultRobocopyOptionSetId = optionSetId });

        var fetched = await settingsRepo.GetAsync();
        Assert.NotNull(fetched);
        Assert.Equal(optionSetId, fetched!.DefaultRobocopyOptionSetId);
        Assert.True(fetched.NotificationsEnabled);
        Assert.Equal(4, fetched.DefaultChecksumWorkers);

        fetched.DefaultChecksumWorkers = 8;
        await settingsRepo.UpdateAsync(fetched);
        Assert.Equal(8, (await settingsRepo.GetAsync())!.DefaultChecksumWorkers);
    }

    [Fact]
    public async Task CredentialTarget_HasNoColumnCapableOfHoldingASecret()
    {
        var repo = new CredentialTargetRepository(_db.Connection);
        var target = new CredentialTarget
        {
            Id = Guid.NewGuid().ToString(), Label = "Synology 1", HostOrUncRoot = @"\\nas1",
            CredentialManagerTargetName = "WindowsBackupHelper:nas1",
        };
        await repo.InsertAsync(target);

        var columns = (await _db.Connection.QueryAsync<string>(
            "SELECT name FROM pragma_table_info('CredentialTarget')")).ToList();

        Assert.DoesNotContain(columns, c => c.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, c => c.Contains("secret", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(columns, c => c.Contains("username", StringComparison.OrdinalIgnoreCase));

        var fetched = await repo.GetByIdAsync(target.Id);
        Assert.Equal("WindowsBackupHelper:nas1", fetched!.CredentialManagerTargetName);
    }

    [Fact]
    public async Task VerificationSettings_ChecksumModeEnum_RoundTripsAsReadableText()
    {
        var repo = new VerificationSettingsRepository(_db.Connection);
        var settings = new VerificationSettings
        {
            Id = Guid.NewGuid().ToString(),
            ChecksumMode = ChecksumMode.VerifyAgainstManifest,
            ChecksumManifestPath = @"D:\Music\checksums.sha256",
            RunFlacAudit = true,
        };
        await repo.InsertAsync(settings);

        var rawValue = await _db.Connection.QuerySingleAsync<string>(
            "SELECT ChecksumMode FROM VerificationSettings WHERE Id = @Id", new { settings.Id });
        Assert.Equal("VerifyAgainstManifest", rawValue);

        var fetched = await repo.GetByIdAsync(settings.Id);
        Assert.Equal(ChecksumMode.VerifyAgainstManifest, fetched!.ChecksumMode);
        Assert.True(fetched.RunFlacAudit);
    }

    [Fact]
    public async Task ScheduleMetadata_TaskNameIsUnique()
    {
        var jobId = Guid.NewGuid().ToString();
        _db.Connection.Execute(
            "INSERT INTO Job (Id, Name, CreatedUtc, UpdatedUtc) VALUES (@Id, 'Job', @Now, @Now)",
            new { Id = jobId, Now = DateTime.UtcNow });

        var repo = new ScheduleMetadataRepository(_db.Connection);
        await repo.InsertAsync(new ScheduleMetadata { JobId = jobId, TaskSchedulerTaskName = @"\WindowsBackupHelper\NightlyMirror" });

        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(() =>
            repo.InsertAsync(new ScheduleMetadata { JobId = jobId, TaskSchedulerTaskName = @"\WindowsBackupHelper\NightlyMirror" }));
    }
}
