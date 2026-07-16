using Dapper;
using Microsoft.Data.Sqlite;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Tests.Data;

namespace WindowsBackupHelper.Core.Tests.Repositories;

public sealed class RunHistoryRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly string _jobId = Guid.NewGuid().ToString();
    private readonly string _folderPairId = Guid.NewGuid().ToString();

    public RunHistoryRepositoryTests()
    {
        _db.Connection.Execute(
            "INSERT INTO Job (Id, Name, CreatedUtc, UpdatedUtc) VALUES (@Id, 'Job', @Now, @Now)",
            new { Id = _jobId, Now = DateTime.UtcNow });
        _db.Connection.Execute(
            "INSERT INTO FolderPair (Id, JobId, SourcePath, DestinationPath) VALUES (@Id, @JobId, 'S', 'D')",
            new { Id = _folderPairId, JobId = _jobId });
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task InsertRunThenChildResult_RoundTrips()
    {
        var runRepo = new RunHistoryRepository(_db.Connection);
        var resultRepo = new FolderPairRunResultRepository(_db.Connection);

        var runId = await runRepo.InsertAsync(new RunHistory
        {
            JobId = _jobId, TriggerType = RunTriggerType.Manual, StartedUtc = DateTime.UtcNow,
        });

        var resultId = await resultRepo.InsertAsync(new FolderPairRunResult
        {
            RunHistoryId = runId, FolderPairId = _folderPairId, StartedUtc = DateTime.UtcNow,
            RobocopyExitCode = 1, FilesCopied = 42, BytesCopied = 123_456_789L,
        });

        var children = await resultRepo.GetByRunHistoryIdAsync(runId);
        Assert.Single(children);
        Assert.Equal(resultId, children[0].Id);
        Assert.Equal(42, children[0].FilesCopied);
    }

    [Fact]
    public async Task CompletingARun_UpdatesOutcomeAndCompletedTimestamp()
    {
        var runRepo = new RunHistoryRepository(_db.Connection);
        var runId = await runRepo.InsertAsync(new RunHistory
        {
            JobId = _jobId, TriggerType = RunTriggerType.Scheduled, StartedUtc = DateTime.UtcNow,
        });

        var run = await runRepo.GetByIdAsync(runId);
        run!.CompletedUtc = DateTime.UtcNow;
        run.OverallOutcome = RunOutcome.Success;
        await runRepo.UpdateAsync(run);

        var fetched = await runRepo.GetByIdAsync(runId);
        Assert.Equal(RunOutcome.Success, fetched!.OverallOutcome);
        Assert.NotNull(fetched.CompletedUtc);
    }

    [Fact]
    public async Task GetByJobId_OrdersNewestFirst()
    {
        var runRepo = new RunHistoryRepository(_db.Connection);
        var older = await runRepo.InsertAsync(new RunHistory
        {
            JobId = _jobId, TriggerType = RunTriggerType.Manual, StartedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        });
        var newer = await runRepo.InsertAsync(new RunHistory
        {
            JobId = _jobId, TriggerType = RunTriggerType.Manual, StartedUtc = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
        });

        var history = await runRepo.GetByJobIdAsync(_jobId);

        Assert.Equal([newer, older], history.Select(r => r.Id));
    }

    [Fact]
    public async Task DeletingARun_CascadesToItsFolderPairRunResults()
    {
        var runRepo = new RunHistoryRepository(_db.Connection);
        var resultRepo = new FolderPairRunResultRepository(_db.Connection);
        var runId = await runRepo.InsertAsync(new RunHistory
        {
            JobId = _jobId, TriggerType = RunTriggerType.Manual, StartedUtc = DateTime.UtcNow,
        });
        await resultRepo.InsertAsync(new FolderPairRunResult
        {
            RunHistoryId = runId, FolderPairId = _folderPairId, StartedUtc = DateTime.UtcNow,
        });

        _db.Connection.Execute("DELETE FROM RunHistory WHERE Id = @Id", new { Id = runId });

        Assert.Empty(await resultRepo.GetByRunHistoryIdAsync(runId));
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheRunAndCascadesToItsResults()
    {
        var runRepo = new RunHistoryRepository(_db.Connection);
        var resultRepo = new FolderPairRunResultRepository(_db.Connection);
        var runId = await runRepo.InsertAsync(new RunHistory
        {
            JobId = _jobId, TriggerType = RunTriggerType.Manual, StartedUtc = DateTime.UtcNow,
        });
        await resultRepo.InsertAsync(new FolderPairRunResult
        {
            RunHistoryId = runId, FolderPairId = _folderPairId, StartedUtc = DateTime.UtcNow,
        });

        await runRepo.DeleteAsync(runId);

        Assert.Null(await runRepo.GetByIdAsync(runId));
        Assert.Empty(await resultRepo.GetByRunHistoryIdAsync(runId));
    }

    [Fact]
    public async Task DeleteAllAsync_RemovesEveryRun()
    {
        var runRepo = new RunHistoryRepository(_db.Connection);
        await runRepo.InsertAsync(new RunHistory
        {
            JobId = _jobId, TriggerType = RunTriggerType.Manual, StartedUtc = DateTime.UtcNow,
        });
        await runRepo.InsertAsync(new RunHistory
        {
            JobId = _jobId, TriggerType = RunTriggerType.Scheduled, StartedUtc = DateTime.UtcNow,
        });

        await runRepo.DeleteAllAsync();

        Assert.Empty(await runRepo.GetByJobIdAsync(_jobId));
    }

    [Fact]
    public async Task DeletingAJob_WithExistingRunHistory_IsBlockedByForeignKey()
    {
        // RunHistory -> Job has no ON DELETE CASCADE by design: a job's audit trail
        // must survive even if someone tries to delete the job, so the FK blocks it
        // rather than silently orphaning or cascading away history.
        var runRepo = new RunHistoryRepository(_db.Connection);
        await runRepo.InsertAsync(new RunHistory
        {
            JobId = _jobId, TriggerType = RunTriggerType.Manual, StartedUtc = DateTime.UtcNow,
        });

        Assert.Throws<SqliteException>(() =>
            _db.Connection.Execute("DELETE FROM Job WHERE Id = @Id", new { Id = _jobId }));
    }
}
