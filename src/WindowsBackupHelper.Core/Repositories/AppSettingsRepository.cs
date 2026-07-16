using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Repositories;

public sealed class AppSettingsRepository(IDbConnection connection)
{
    public Task<AppSettings?> GetAsync() =>
        connection.QuerySingleOrDefaultAsync<AppSettings>(
            "SELECT * FROM AppSettings WHERE Id = @Id", new { Id = Models.AppSettings.SingletonId });

    public Task InsertAsync(AppSettings settings) =>
        connection.ExecuteAsync(
            """
            INSERT INTO AppSettings (
                Id, FlacExecutablePath, DefaultRobocopyOptionSetId, NotificationsEnabled,
                DefaultChecksumWorkers, DefaultFlacWorkers
            ) VALUES (
                @Id, @FlacExecutablePath, @DefaultRobocopyOptionSetId, @NotificationsEnabled,
                @DefaultChecksumWorkers, @DefaultFlacWorkers
            )
            """,
            settings);

    public Task UpdateAsync(AppSettings settings) =>
        connection.ExecuteAsync(
            """
            UPDATE AppSettings SET
                FlacExecutablePath = @FlacExecutablePath,
                DefaultRobocopyOptionSetId = @DefaultRobocopyOptionSetId,
                NotificationsEnabled = @NotificationsEnabled,
                DefaultChecksumWorkers = @DefaultChecksumWorkers,
                DefaultFlacWorkers = @DefaultFlacWorkers
            WHERE Id = @Id
            """,
            settings);
}
