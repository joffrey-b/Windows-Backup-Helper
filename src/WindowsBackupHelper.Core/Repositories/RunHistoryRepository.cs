using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Repositories;

public sealed class RunHistoryRepository(IDbConnection connection)
{
    public Task<RunHistory?> GetByIdAsync(int id) =>
        connection.QuerySingleOrDefaultAsync<RunHistory>("SELECT * FROM RunHistory WHERE Id = @Id", new { Id = id });

    public async Task<IReadOnlyList<RunHistory>> GetByJobIdAsync(string jobId) =>
        (await connection.QueryAsync<RunHistory>(
            "SELECT * FROM RunHistory WHERE JobId = @JobId ORDER BY StartedUtc DESC", new { JobId = jobId })).AsList();

    public async Task<int> InsertAsync(RunHistory run)
    {
        // Dapper converts enum-typed POCO properties to their underlying integer
        // representation before a registered SqlMapper.TypeHandler gets a chance to run,
        // so TriggerType/OverallOutcome are stringified explicitly here rather than
        // relying on StringEnumTypeHandler for the parameter (write) direction.
        var id = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO RunHistory (JobId, TriggerType, StartedUtc, CompletedUtc, WasDryRun, OverallOutcome, Notes)
            VALUES (@JobId, @TriggerType, @StartedUtc, @CompletedUtc, @WasDryRun, @OverallOutcome, @Notes);
            SELECT last_insert_rowid();
            """,
            new
            {
                run.JobId,
                TriggerType = run.TriggerType.ToString(),
                run.StartedUtc,
                run.CompletedUtc,
                run.WasDryRun,
                OverallOutcome = run.OverallOutcome?.ToString(),
                run.Notes,
            });
        return (int)id;
    }

    public Task UpdateAsync(RunHistory run) =>
        connection.ExecuteAsync(
            """
            UPDATE RunHistory SET
                CompletedUtc = @CompletedUtc, OverallOutcome = @OverallOutcome, Notes = @Notes
            WHERE Id = @Id
            """,
            new
            {
                run.Id,
                run.CompletedUtc,
                OverallOutcome = run.OverallOutcome?.ToString(),
                run.Notes,
            });

    /// <summary>Hard delete: unlike Job/FolderPair, nothing references RunHistory as an audit
    /// trail that must survive it — it IS the audit trail. FolderPairRunResult.RunHistoryId
    /// cascades (ON DELETE CASCADE), so this is safe.</summary>
    public Task DeleteAsync(int id) =>
        connection.ExecuteAsync("DELETE FROM RunHistory WHERE Id = @Id", new { Id = id });

    public Task DeleteAllAsync() =>
        connection.ExecuteAsync("DELETE FROM RunHistory");
}
