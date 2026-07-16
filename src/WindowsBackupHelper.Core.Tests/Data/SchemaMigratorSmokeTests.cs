using Dapper;
using Microsoft.Data.Sqlite;
using WindowsBackupHelper.Core.Data;

namespace WindowsBackupHelper.Core.Tests.Data;

/// <summary>
/// Validates that Microsoft.Data.Sqlite + the migrator + Dapper behave as expected in
/// this environment (real temp-file databases, not in-memory) before the repository
/// layer is built on top of them.
/// </summary>
public sealed class SchemaMigratorSmokeTests : IDisposable
{
    private readonly string _databasePath;

    public SchemaMigratorSmokeTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"wbh-smoke-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            var path = _databasePath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Migrate_CreatesSchemaThatAcceptsRoundTrippedData()
    {
        var factory = new SqliteConnectionFactory(_databasePath);
        using var connection = factory.OpenConnection();
        new SchemaMigrator().Migrate(connection);

        var optionSetId = Guid.NewGuid().ToString();
        connection.Execute(
            "INSERT INTO RobocopyOptionSet (Id, Mirror, Retries, WaitSeconds) VALUES (@Id, @Mirror, @Retries, @WaitSeconds)",
            new { Id = optionSetId, Mirror = true, Retries = 3, WaitSeconds = 5 });

        connection.Execute(
            "INSERT INTO AppSettings (Id, DefaultRobocopyOptionSetId) VALUES (1, @OptionSetId)",
            new { OptionSetId = optionSetId });

        var mirror = connection.QuerySingle<bool>(
            "SELECT Mirror FROM RobocopyOptionSet WHERE Id = @Id", new { Id = optionSetId });
        Assert.True(mirror);

        var resolvedOptionSetId = connection.QuerySingle<string>(
            "SELECT DefaultRobocopyOptionSetId FROM AppSettings WHERE Id = 1");
        Assert.Equal(optionSetId, resolvedOptionSetId);
    }

    [Fact]
    public void Migrate_IsIdempotent()
    {
        var factory = new SqliteConnectionFactory(_databasePath);
        using var connection = factory.OpenConnection();
        var migrator = new SchemaMigrator();

        migrator.Migrate(connection);
        migrator.Migrate(connection); // must not attempt to re-run earlier migrations and fail on "table already exists"

        var userVersion = connection.QuerySingle<long>("PRAGMA user_version;");
        Assert.Equal(1, userVersion); // bump alongside the migrations folder's highest version number
    }

    [Fact]
    public void OpenConnection_EnablesWalAndForeignKeys()
    {
        var factory = new SqliteConnectionFactory(_databasePath);
        using var connection = factory.OpenConnection();

        var journalMode = connection.QuerySingle<string>("PRAGMA journal_mode;");
        Assert.Equal("wal", journalMode, ignoreCase: true);

        var foreignKeys = connection.QuerySingle<long>("PRAGMA foreign_keys;");
        Assert.Equal(1, foreignKeys);
    }

    [Fact]
    public void AppSettings_RejectsSecondSingletonRow()
    {
        var factory = new SqliteConnectionFactory(_databasePath);
        using var connection = factory.OpenConnection();
        new SchemaMigrator().Migrate(connection);

        var optionSetId = Guid.NewGuid().ToString();
        connection.Execute("INSERT INTO RobocopyOptionSet (Id) VALUES (@Id)", new { Id = optionSetId });
        connection.Execute(
            "INSERT INTO AppSettings (Id, DefaultRobocopyOptionSetId) VALUES (1, @OptionSetId)",
            new { OptionSetId = optionSetId });

        Assert.ThrowsAny<Microsoft.Data.Sqlite.SqliteException>(() =>
            connection.Execute(
                "INSERT INTO AppSettings (Id, DefaultRobocopyOptionSetId) VALUES (2, @OptionSetId)",
                new { OptionSetId = optionSetId }));
    }
}
