using Microsoft.Data.Sqlite;

namespace WindowsBackupHelper.Core.Data;

/// <summary>
/// Opens SQLite connections with the PRAGMAs this app relies on for correctness under
/// concurrent access: WAL so the interactive GUI and a headless scheduled run can open
/// the database at the same time, busy_timeout so one writer doesn't fail instantly
/// under contention, and foreign_keys because SQLite does not enforce them by default.
/// </summary>
public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;
    private readonly int _busyTimeoutMilliseconds;

    public SqliteConnectionFactory(string databasePath, TimeSpan? busyTimeout = null)
    {
        DapperTypeHandlers.RegisterAll();

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();
        _busyTimeoutMilliseconds = (int)(busyTimeout ?? TimeSpan.FromSeconds(10)).TotalMilliseconds;
    }

    public SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var pragmaCommand = connection.CreateCommand();
        pragmaCommand.CommandText =
            $"""
             PRAGMA foreign_keys = ON;
             PRAGMA journal_mode = WAL;
             PRAGMA busy_timeout = {_busyTimeoutMilliseconds};
             """;
        pragmaCommand.ExecuteNonQuery();

        return connection;
    }
}
