using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Repositories;

public sealed class FolderPairRepository(IDbConnection connection)
{
    /// <summary>Fetches by id regardless of soft-delete status — Run History needs to look up
    /// a historical run's pair even if it's since been deleted, to show whether it's still active.</summary>
    public Task<FolderPair?> GetByIdAsync(string id) =>
        connection.QuerySingleOrDefaultAsync<FolderPair>("SELECT * FROM FolderPair WHERE Id = @Id", new { Id = id });

    /// <summary>Active (not soft-deleted) pairs only — the Folder Pairs grid's list.</summary>
    public async Task<IReadOnlyList<FolderPair>> GetByJobIdAsync(string jobId) =>
        (await connection.QueryAsync<FolderPair>(
            "SELECT * FROM FolderPair WHERE JobId = @JobId AND IsDeleted = 0 ORDER BY SortOrder", new { JobId = jobId })).AsList();

    public Task InsertAsync(FolderPair pair) =>
        connection.ExecuteAsync(
            """
            INSERT INTO FolderPair (
                Id, JobId, Name, SourcePath, DestinationPath, SourceCredentialTargetId,
                DestinationCredentialTargetId, PairRobocopyOptionSetId, VerificationSettingsId,
                SortOrder, IsEnabled
            ) VALUES (
                @Id, @JobId, @Name, @SourcePath, @DestinationPath, @SourceCredentialTargetId,
                @DestinationCredentialTargetId, @PairRobocopyOptionSetId, @VerificationSettingsId,
                @SortOrder, @IsEnabled
            )
            """,
            pair);

    public Task UpdateAsync(FolderPair pair) =>
        connection.ExecuteAsync(
            """
            UPDATE FolderPair SET
                Name = @Name, SourcePath = @SourcePath, DestinationPath = @DestinationPath,
                SourceCredentialTargetId = @SourceCredentialTargetId,
                DestinationCredentialTargetId = @DestinationCredentialTargetId,
                PairRobocopyOptionSetId = @PairRobocopyOptionSetId,
                VerificationSettingsId = @VerificationSettingsId, SortOrder = @SortOrder, IsEnabled = @IsEnabled
            WHERE Id = @Id
            """,
            pair);

    /// <summary>Soft-delete: a hard DELETE would violate FOREIGN KEY constraints for any pair
    /// with run history (FolderPairRunResult.FolderPairId references it). This just hides the
    /// pair from GetByJobIdAsync's active list instead, preserving the audit trail.</summary>
    public Task DeleteAsync(string id) =>
        connection.ExecuteAsync("UPDATE FolderPair SET IsDeleted = 1 WHERE Id = @Id", new { Id = id });

    /// <summary>Nulls out any pair's reference to a credential about to be deleted -- including
    /// soft-deleted pairs, whose row (and FK) still exists. Without this, deleting a
    /// CredentialTarget still referenced by any FolderPair.Source/DestinationCredentialTargetId
    /// throws a foreign key violation, since that FK has no ON DELETE CASCADE/SET NULL.</summary>
    public Task ClearCredentialReferencesAsync(string credentialTargetId) =>
        connection.ExecuteAsync(
            """
            UPDATE FolderPair SET SourceCredentialTargetId = NULL WHERE SourceCredentialTargetId = @Id;
            UPDATE FolderPair SET DestinationCredentialTargetId = NULL WHERE DestinationCredentialTargetId = @Id;
            """,
            new { Id = credentialTargetId });
}
