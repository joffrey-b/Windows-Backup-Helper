using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Repositories;

public sealed class JobRepository(IDbConnection connection)
{
    public Task<Job?> GetByIdAsync(string id) =>
        connection.QuerySingleOrDefaultAsync<Job>("SELECT * FROM Job WHERE Id = @Id", new { Id = id });

    /// <summary>Active (not soft-deleted) jobs only — the Jobs tab's list.</summary>
    public async Task<IReadOnlyList<Job>> GetAllAsync() =>
        (await connection.QueryAsync<Job>("SELECT * FROM Job WHERE IsDeleted = 0 ORDER BY SortOrder")).AsList();

    /// <summary>Every job regardless of soft-delete status — Run History needs to show
    /// whether a historical run's job is still active, so it can't filter deleted ones out.</summary>
    public async Task<IReadOnlyList<Job>> GetAllIncludingDeletedAsync() =>
        (await connection.QueryAsync<Job>("SELECT * FROM Job ORDER BY SortOrder")).AsList();

    public Task InsertAsync(Job job) =>
        connection.ExecuteAsync(
            """
            INSERT INTO Job (Id, Name, Description, JobRobocopyOptionSetId, IsEnabled, SortOrder, CreatedUtc, UpdatedUtc)
            VALUES (@Id, @Name, @Description, @JobRobocopyOptionSetId, @IsEnabled, @SortOrder, @CreatedUtc, @UpdatedUtc)
            """,
            job);

    public Task UpdateAsync(Job job) =>
        connection.ExecuteAsync(
            """
            UPDATE Job SET
                Name = @Name, Description = @Description, JobRobocopyOptionSetId = @JobRobocopyOptionSetId,
                IsEnabled = @IsEnabled, SortOrder = @SortOrder, UpdatedUtc = @UpdatedUtc
            WHERE Id = @Id
            """,
            job);

    /// <summary>Soft-delete: a hard DELETE would violate FOREIGN KEY constraints for any job
    /// with run history (RunHistory.JobId references it directly). This just hides the job
    /// (and cascades to hide its folder pairs too, matching the FK schema's original ON DELETE
    /// CASCADE intent) from the active lists instead, preserving the audit trail.</summary>
    public Task DeleteAsync(string id) =>
        connection.ExecuteAsync(
            """
            UPDATE Job SET IsDeleted = 1 WHERE Id = @Id;
            UPDATE FolderPair SET IsDeleted = 1 WHERE JobId = @Id;
            """,
            new { Id = id });
}
