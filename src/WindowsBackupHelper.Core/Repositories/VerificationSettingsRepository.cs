using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Repositories;

public sealed class VerificationSettingsRepository(IDbConnection connection)
{
    public Task<VerificationSettings?> GetByIdAsync(string id) =>
        connection.QuerySingleOrDefaultAsync<VerificationSettings>(
            "SELECT * FROM VerificationSettings WHERE Id = @Id", new { Id = id });

    public Task InsertAsync(VerificationSettings settings) =>
        // Dapper converts enum-typed POCO properties to their underlying integer
        // representation before a registered SqlMapper.TypeHandler gets a chance to run,
        // so ChecksumMode is stringified explicitly here rather than relying on
        // StringEnumTypeHandler for the parameter (write) direction.
        connection.ExecuteAsync(
            """
            INSERT INTO VerificationSettings (
                Id, ChecksumMode, ChecksumManifestPath, ChecksumWorkers, ChecksumReportOutputPath,
                RunFlacAudit, FlacReportOutputPath, FlacErrorsOnly, FlacWorkers
            ) VALUES (
                @Id, @ChecksumMode, @ChecksumManifestPath, @ChecksumWorkers, @ChecksumReportOutputPath,
                @RunFlacAudit, @FlacReportOutputPath, @FlacErrorsOnly, @FlacWorkers
            )
            """,
            new
            {
                settings.Id,
                ChecksumMode = settings.ChecksumMode.ToString(),
                settings.ChecksumManifestPath,
                settings.ChecksumWorkers,
                settings.ChecksumReportOutputPath,
                settings.RunFlacAudit,
                settings.FlacReportOutputPath,
                settings.FlacErrorsOnly,
                settings.FlacWorkers,
            });

    public Task UpdateAsync(VerificationSettings settings) =>
        connection.ExecuteAsync(
            """
            UPDATE VerificationSettings SET
                ChecksumMode = @ChecksumMode, ChecksumManifestPath = @ChecksumManifestPath,
                ChecksumWorkers = @ChecksumWorkers, ChecksumReportOutputPath = @ChecksumReportOutputPath,
                RunFlacAudit = @RunFlacAudit, FlacReportOutputPath = @FlacReportOutputPath,
                FlacErrorsOnly = @FlacErrorsOnly, FlacWorkers = @FlacWorkers
            WHERE Id = @Id
            """,
            new
            {
                settings.Id,
                ChecksumMode = settings.ChecksumMode.ToString(),
                settings.ChecksumManifestPath,
                settings.ChecksumWorkers,
                settings.ChecksumReportOutputPath,
                settings.RunFlacAudit,
                settings.FlacReportOutputPath,
                settings.FlacErrorsOnly,
                settings.FlacWorkers,
            });

    public Task DeleteAsync(string id) =>
        connection.ExecuteAsync("DELETE FROM VerificationSettings WHERE Id = @Id", new { Id = id });
}
