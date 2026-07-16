using Microsoft.Data.Sqlite;
using WindowsBackupHelper.Core.Data;

namespace WindowsBackupHelper.Core.Tests.Data;

/// <summary>
/// A real temp-file SQLite database, migrated and ready to use. Deliberately not
/// in-memory, so repository tests exercise the same file-based code path production
/// (and the interactive-GUI/headless-scheduled-run concurrency it depends on) uses.
/// </summary>
public sealed class SqliteTestDatabase : IDisposable
{
    private readonly string _databasePath;

    public SqliteConnection Connection { get; }

    public SqliteTestDatabase()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"wbh-test-{Guid.NewGuid():N}.db");
        Connection = new SqliteConnectionFactory(_databasePath).OpenConnection();
        new SchemaMigrator().Migrate(Connection);
    }

    public void Dispose()
    {
        Connection.Dispose();
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
}
