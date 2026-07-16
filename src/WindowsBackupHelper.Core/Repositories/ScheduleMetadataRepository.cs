using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Repositories;

public sealed class ScheduleMetadataRepository(IDbConnection connection)
{
    public Task<ScheduleMetadata?> GetByJobIdAsync(string jobId) =>
        connection.QuerySingleOrDefaultAsync<ScheduleMetadata>(
            "SELECT * FROM ScheduleMetadata WHERE JobId = @JobId", new { JobId = jobId });

    public async Task<IReadOnlyList<ScheduleMetadata>> GetAllAsync() =>
        (await connection.QueryAsync<ScheduleMetadata>("SELECT * FROM ScheduleMetadata")).AsList();

    public async Task<int> InsertAsync(ScheduleMetadata metadata)
    {
        var id = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO ScheduleMetadata (
                JobId, TaskSchedulerTaskName, TriggerDescription, IsEnabled,
                RunWhetherUserLoggedOnOrNot, LastSyncedUtc
            ) VALUES (
                @JobId, @TaskSchedulerTaskName, @TriggerDescription, @IsEnabled,
                @RunWhetherUserLoggedOnOrNot, @LastSyncedUtc
            );
            SELECT last_insert_rowid();
            """,
            metadata);
        return (int)id;
    }

    public Task UpdateAsync(ScheduleMetadata metadata) =>
        connection.ExecuteAsync(
            """
            UPDATE ScheduleMetadata SET
                TriggerDescription = @TriggerDescription, IsEnabled = @IsEnabled,
                RunWhetherUserLoggedOnOrNot = @RunWhetherUserLoggedOnOrNot, LastSyncedUtc = @LastSyncedUtc
            WHERE Id = @Id
            """,
            metadata);

    public Task DeleteAsync(int id) =>
        connection.ExecuteAsync("DELETE FROM ScheduleMetadata WHERE Id = @Id", new { Id = id });
}
