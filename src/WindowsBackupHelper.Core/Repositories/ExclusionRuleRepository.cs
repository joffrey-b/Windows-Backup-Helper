using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Repositories;

public sealed class ExclusionRuleRepository(IDbConnection connection)
{
    public Task<ExclusionRule?> GetByIdAsync(int id) =>
        connection.QuerySingleOrDefaultAsync<ExclusionRule>("SELECT * FROM ExclusionRule WHERE Id = @Id", new { Id = id });

    /// <summary>
    /// The union of Global + Job(jobId) + FolderPair(folderPairId) scoped rules — the set an
    /// ExclusionRuleResolver run for this folder pair must evaluate. Rules across scopes are
    /// additive, unlike the RobocopyOptionSet cascade.
    /// </summary>
    public async Task<IReadOnlyList<ExclusionRule>> GetApplicableRulesAsync(string jobId, string folderPairId) =>
        (await connection.QueryAsync<ExclusionRule>(
            """
            SELECT * FROM ExclusionRule
            WHERE Scope = 'Global'
               OR (Scope = 'Job' AND JobId = @JobId)
               OR (Scope = 'FolderPair' AND FolderPairId = @FolderPairId)
            ORDER BY Scope, SortOrder
            """,
            new { JobId = jobId, FolderPairId = folderPairId })).AsList();

    public async Task<IReadOnlyList<ExclusionRule>> GetByJobIdAsync(string jobId) =>
        (await connection.QueryAsync<ExclusionRule>(
            "SELECT * FROM ExclusionRule WHERE Scope = 'Job' AND JobId = @JobId ORDER BY SortOrder",
            new { JobId = jobId })).AsList();

    public async Task<IReadOnlyList<ExclusionRule>> GetByFolderPairIdAsync(string folderPairId) =>
        (await connection.QueryAsync<ExclusionRule>(
            "SELECT * FROM ExclusionRule WHERE Scope = 'FolderPair' AND FolderPairId = @FolderPairId ORDER BY SortOrder",
            new { FolderPairId = folderPairId })).AsList();

    public async Task<IReadOnlyList<ExclusionRule>> GetGlobalRulesAsync() =>
        (await connection.QueryAsync<ExclusionRule>(
            "SELECT * FROM ExclusionRule WHERE Scope = 'Global' ORDER BY SortOrder")).AsList();

    public async Task<int> InsertAsync(ExclusionRule rule)
    {
        // Dapper converts enum-typed POCO properties to their underlying integer
        // representation before a registered SqlMapper.TypeHandler gets a chance to run,
        // so the enum columns are stringified explicitly here rather than relying on
        // StringEnumTypeHandler for the parameter (write) direction.
        var id = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO ExclusionRule (Scope, JobId, FolderPairId, PatternType, Pattern, TargetType, IsEnabled, Description, SortOrder)
            VALUES (@Scope, @JobId, @FolderPairId, @PatternType, @Pattern, @TargetType, @IsEnabled, @Description, @SortOrder);
            SELECT last_insert_rowid();
            """,
            new
            {
                Scope = rule.Scope.ToString(),
                rule.JobId,
                rule.FolderPairId,
                PatternType = rule.PatternType.ToString(),
                rule.Pattern,
                TargetType = rule.TargetType.ToString(),
                rule.IsEnabled,
                rule.Description,
                rule.SortOrder,
            });
        return (int)id;
    }

    public Task UpdateAsync(ExclusionRule rule) =>
        connection.ExecuteAsync(
            """
            UPDATE ExclusionRule SET
                Scope = @Scope, JobId = @JobId, FolderPairId = @FolderPairId, PatternType = @PatternType,
                Pattern = @Pattern, TargetType = @TargetType, IsEnabled = @IsEnabled,
                Description = @Description, SortOrder = @SortOrder
            WHERE Id = @Id
            """,
            new
            {
                rule.Id,
                Scope = rule.Scope.ToString(),
                rule.JobId,
                rule.FolderPairId,
                PatternType = rule.PatternType.ToString(),
                rule.Pattern,
                TargetType = rule.TargetType.ToString(),
                rule.IsEnabled,
                rule.Description,
                rule.SortOrder,
            });

    public Task DeleteAsync(int id) =>
        connection.ExecuteAsync("DELETE FROM ExclusionRule WHERE Id = @Id", new { Id = id });
}
