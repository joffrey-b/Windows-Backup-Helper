using Microsoft.Data.Sqlite;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Tests.Data;

namespace WindowsBackupHelper.Core.Tests.Repositories;

public sealed class JobAndFolderPairRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private static Job NewJob(string? name = null, int sortOrder = 0) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Name = name ?? $"Job-{Guid.NewGuid():N}",
        SortOrder = sortOrder,
        CreatedUtc = DateTime.UtcNow,
        UpdatedUtc = DateTime.UtcNow,
    };

    private static FolderPair NewFolderPair(string jobId, int sortOrder = 0) => new()
    {
        Id = Guid.NewGuid().ToString(),
        JobId = jobId,
        SourcePath = @"\\nas\Music",
        DestinationPath = @"D:\Backup\Music",
        SortOrder = sortOrder,
    };

    [Fact]
    public async Task InsertAndGetById_RoundTripsAllColumns()
    {
        var repo = new JobRepository(_db.Connection);
        var job = NewJob("Nightly Mirror");
        job.Description = "Mirrors the NAS music library nightly.";

        await repo.InsertAsync(job);
        var fetched = await repo.GetByIdAsync(job.Id);

        Assert.NotNull(fetched);
        Assert.Equal(job.Name, fetched!.Name);
        Assert.Equal(job.Description, fetched.Description);
        Assert.True(fetched.IsEnabled);
    }

    [Fact]
    public async Task GetAll_OrdersBySortOrder()
    {
        var repo = new JobRepository(_db.Connection);
        var second = NewJob("Second", sortOrder: 1);
        var first = NewJob("First", sortOrder: 0);
        await repo.InsertAsync(second);
        await repo.InsertAsync(first);

        var all = await repo.GetAllAsync();

        Assert.Equal(["First", "Second"], all.Select(j => j.Name));
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        var repo = new JobRepository(_db.Connection);
        var job = NewJob();
        await repo.InsertAsync(job);

        job.Name = "Renamed";
        job.IsEnabled = false;
        await repo.UpdateAsync(job);

        var fetched = await repo.GetByIdAsync(job.Id);
        Assert.Equal("Renamed", fetched!.Name);
        Assert.False(fetched.IsEnabled);
    }

    [Fact]
    public async Task DeletingJob_CascadesToItsFolderPairs()
    {
        var jobRepo = new JobRepository(_db.Connection);
        var pairRepo = new FolderPairRepository(_db.Connection);
        var job = NewJob();
        await jobRepo.InsertAsync(job);
        var pair = NewFolderPair(job.Id);
        await pairRepo.InsertAsync(pair);

        await jobRepo.DeleteAsync(job.Id);

        Assert.Empty(await pairRepo.GetByJobIdAsync(job.Id));
    }

    [Fact]
    public async Task DeletingJob_WithExistingRunHistory_SoftDeletesInsteadOfThrowing()
    {
        var jobRepo = new JobRepository(_db.Connection);
        var runRepo = new RunHistoryRepository(_db.Connection);
        var job = NewJob();
        await jobRepo.InsertAsync(job);
        await runRepo.InsertAsync(new RunHistory
        {
            JobId = job.Id, TriggerType = RunTriggerType.Manual, StartedUtc = DateTime.UtcNow,
        });

        await jobRepo.DeleteAsync(job.Id);

        Assert.DoesNotContain(await jobRepo.GetAllAsync(), j => j.Id == job.Id);
        var stillThere = (await jobRepo.GetAllIncludingDeletedAsync()).Single(j => j.Id == job.Id);
        Assert.True(stillThere.IsDeleted);
    }

    [Fact]
    public async Task DeletingFolderPair_WithExistingRunResult_SoftDeletesInsteadOfThrowing()
    {
        var jobRepo = new JobRepository(_db.Connection);
        var pairRepo = new FolderPairRepository(_db.Connection);
        var runRepo = new RunHistoryRepository(_db.Connection);
        var resultRepo = new FolderPairRunResultRepository(_db.Connection);

        var job = NewJob();
        await jobRepo.InsertAsync(job);
        var pair = NewFolderPair(job.Id);
        await pairRepo.InsertAsync(pair);
        var runId = await runRepo.InsertAsync(new RunHistory
        {
            JobId = job.Id, TriggerType = RunTriggerType.Manual, StartedUtc = DateTime.UtcNow,
        });
        await resultRepo.InsertAsync(new FolderPairRunResult
        {
            RunHistoryId = runId, FolderPairId = pair.Id, StartedUtc = DateTime.UtcNow,
        });

        await pairRepo.DeleteAsync(pair.Id);

        Assert.Empty(await pairRepo.GetByJobIdAsync(job.Id));
        var stillThere = await pairRepo.GetByIdAsync(pair.Id);
        Assert.True(stillThere!.IsDeleted);
    }

    [Fact]
    public async Task InsertingJob_WithNameOfASoftDeletedJob_Succeeds()
    {
        // Regression test: clicking "Add job" right after deleting a job named e.g. "New Job 1"
        // used to crash the whole app with a UNIQUE constraint violation, because the
        // soft-deleted row was still sitting in the table occupying that name.
        var repo = new JobRepository(_db.Connection);
        var deleted = NewJob("Reused Name");
        await repo.InsertAsync(deleted);
        await repo.DeleteAsync(deleted.Id);

        var reused = NewJob("Reused Name");
        await repo.InsertAsync(reused);

        var fetched = await repo.GetByIdAsync(reused.Id);
        Assert.Equal("Reused Name", fetched!.Name);
    }

    [Fact]
    public async Task InsertingJob_WithNameOfAnActiveJob_StillThrows()
    {
        var repo = new JobRepository(_db.Connection);
        await repo.InsertAsync(NewJob("Taken Name"));

        await Assert.ThrowsAsync<SqliteException>(() => repo.InsertAsync(NewJob("Taken Name")));
    }

    [Fact]
    public async Task RenamingJob_ToAnotherActiveJobsName_Throws()
    {
        // Regression test: renaming "New Job 2" to "New Job 1" while "New Job 1" already exists
        // used to crash the whole app with an uncaught UNIQUE constraint violation.
        var repo = new JobRepository(_db.Connection);
        await repo.InsertAsync(NewJob("New Job 1"));
        var second = NewJob("New Job 2");
        await repo.InsertAsync(second);

        second.Name = "New Job 1";

        await Assert.ThrowsAsync<SqliteException>(() => repo.UpdateAsync(second));
    }

    [Fact]
    public async Task FolderPair_TwoIndependentCredentialTargets_ForTwoNasHosts()
    {
        var jobRepo = new JobRepository(_db.Connection);
        var credRepo = new CredentialTargetRepository(_db.Connection);
        var pairRepo = new FolderPairRepository(_db.Connection);

        var job = NewJob();
        await jobRepo.InsertAsync(job);

        var sourceCred = new CredentialTarget
        {
            Id = Guid.NewGuid().ToString(), Label = "NAS 1", HostOrUncRoot = @"\\nas1",
            CredentialManagerTargetName = "WindowsBackupHelper:nas1",
        };
        var destCred = new CredentialTarget
        {
            Id = Guid.NewGuid().ToString(), Label = "NAS 2", HostOrUncRoot = @"\\nas2",
            CredentialManagerTargetName = "WindowsBackupHelper:nas2",
        };
        await credRepo.InsertAsync(sourceCred);
        await credRepo.InsertAsync(destCred);

        var pair = NewFolderPair(job.Id);
        pair.SourceCredentialTargetId = sourceCred.Id;
        pair.DestinationCredentialTargetId = destCred.Id;
        await pairRepo.InsertAsync(pair);

        var fetched = await pairRepo.GetByIdAsync(pair.Id);
        Assert.Equal(sourceCred.Id, fetched!.SourceCredentialTargetId);
        Assert.Equal(destCred.Id, fetched.DestinationCredentialTargetId);
        Assert.NotEqual(fetched.SourceCredentialTargetId, fetched.DestinationCredentialTargetId);
    }

    [Fact]
    public async Task ClearCredentialReferencesAsync_NullsOutBothSourceAndDestinationReferences_IncludingOnSoftDeletedPairs()
    {
        // Regression test: deleting a credential still referenced by a FolderPair used to throw
        // a foreign key violation and crash the app, since Source/DestinationCredentialTargetId
        // has no ON DELETE CASCADE/SET NULL. A soft-deleted pair's row (and FK) still exists
        // too, so it must be cleared just as much as an active one's.
        var jobRepo = new JobRepository(_db.Connection);
        var credRepo = new CredentialTargetRepository(_db.Connection);
        var pairRepo = new FolderPairRepository(_db.Connection);

        var job = NewJob();
        await jobRepo.InsertAsync(job);

        var credential = new CredentialTarget
        {
            Id = Guid.NewGuid().ToString(), Label = "NAS", HostOrUncRoot = @"\\nas",
            CredentialManagerTargetName = "WindowsBackupHelper:shared-nas",
        };
        await credRepo.InsertAsync(credential);

        var sourcePair = NewFolderPair(job.Id);
        sourcePair.SourceCredentialTargetId = credential.Id;
        await pairRepo.InsertAsync(sourcePair);

        var destinationPair = NewFolderPair(job.Id, sortOrder: 1);
        destinationPair.DestinationCredentialTargetId = credential.Id;
        await pairRepo.InsertAsync(destinationPair);
        await pairRepo.DeleteAsync(destinationPair.Id); // soft-deleted, but the row/FK still exists

        await pairRepo.ClearCredentialReferencesAsync(credential.Id);

        Assert.Null((await pairRepo.GetByIdAsync(sourcePair.Id))!.SourceCredentialTargetId);
        Assert.Null((await pairRepo.GetByIdAsync(destinationPair.Id))!.DestinationCredentialTargetId);

        // The actual regression: this must not throw now.
        await credRepo.DeleteAsync(credential.Id);
    }

    [Fact]
    public async Task InsertingFolderPair_ForNonexistentJob_ThrowsForeignKeyViolation()
    {
        var pairRepo = new FolderPairRepository(_db.Connection);
        var orphan = NewFolderPair(Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<SqliteException>(() => pairRepo.InsertAsync(orphan));
    }
}
