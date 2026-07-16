using System.Reflection;
using Microsoft.Data.Sqlite;

namespace WindowsBackupHelper.Core.Data;

/// <summary>
/// Applies versioned .sql migrations (embedded resources under Data/Migrations, named
/// "NNNN_description.sql") tracked via SQLite's PRAGMA user_version. Plain, reviewable
/// SQL files rather than an ORM's change-tracking migrations, consistent with this
/// project's "transparent tool" goal.
/// </summary>
public sealed class SchemaMigrator
{
    private const string MigrationsMarker = ".Data.Migrations.";

    private readonly Assembly _resourceAssembly;

    public SchemaMigrator(Assembly? resourceAssembly = null)
    {
        _resourceAssembly = resourceAssembly ?? typeof(SchemaMigrator).Assembly;
    }

    public void Migrate(SqliteConnection connection)
    {
        var pendingMigrations = DiscoverMigrations()
            .Where(m => m.Version > GetUserVersion(connection))
            .OrderBy(m => m.Version);

        foreach (var migration in pendingMigrations)
        {
            using var transaction = connection.BeginTransaction();

            using (var stream = _resourceAssembly.GetManifestResourceStream(migration.ResourceName))
            {
                if (stream is null)
                {
                    throw new InvalidOperationException($"Migration resource '{migration.ResourceName}' could not be loaded.");
                }

                using var reader = new StreamReader(stream);
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = reader.ReadToEnd();
                command.ExecuteNonQuery();
            }

            using (var pragmaCommand = connection.CreateCommand())
            {
                pragmaCommand.Transaction = transaction;
                // PRAGMA does not accept bound parameters; migration.Version comes only
                // from our own embedded resource filenames, never from user input.
                pragmaCommand.CommandText = $"PRAGMA user_version = {migration.Version};";
                pragmaCommand.ExecuteNonQuery();
            }

            transaction.Commit();
        }
    }

    private List<(int Version, string ResourceName)> DiscoverMigrations()
    {
        var migrations = new List<(int Version, string ResourceName)>();

        foreach (var resourceName in _resourceAssembly.GetManifestResourceNames())
        {
            var markerIndex = resourceName.IndexOf(MigrationsMarker, StringComparison.Ordinal);
            if (markerIndex < 0 || !resourceName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileName = resourceName[(markerIndex + MigrationsMarker.Length)..];
            var underscoreIndex = fileName.IndexOf('_');
            if (underscoreIndex <= 0 || !int.TryParse(fileName[..underscoreIndex], out var version))
            {
                continue;
            }

            migrations.Add((version, resourceName));
        }

        return migrations;
    }

    private static long GetUserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt64(command.ExecuteScalar());
    }
}
