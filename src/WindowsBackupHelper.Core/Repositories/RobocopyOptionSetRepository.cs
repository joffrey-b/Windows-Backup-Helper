using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Data;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Repositories;

public sealed class RobocopyOptionSetRepository(IDbConnection connection)
{
    public Task<RobocopyOptionSet?> GetByIdAsync(string id) =>
        connection.QuerySingleOrDefaultAsync<RobocopyOptionSet>(
            "SELECT * FROM RobocopyOptionSet WHERE Id = @Id", new { Id = id });

    /// <summary>
    /// Fetches the app-level default RobocopyOptionSet AppSettings points at, throwing if it's
    /// somehow missing (DB corruption, or a row deleted out from under the FK) — every job's
    /// option resolution ultimately falls back to this row, so callers can't proceed without it.
    /// </summary>
    public async Task<RobocopyOptionSet> GetRequiredDefaultAsync(AppSettingsCache appSettingsCache) =>
        await GetByIdAsync(appSettingsCache.Current.DefaultRobocopyOptionSetId).ConfigureAwait(false)
            ?? throw new InvalidOperationException("AppSettings' default RobocopyOptionSet is missing.");

    public Task InsertAsync(RobocopyOptionSet optionSet) =>
        connection.ExecuteAsync(
            """
            INSERT INTO RobocopyOptionSet (
                Id, Mirror, CopySubdirectories, CopyEmptySubdirectories, Purge, Move, MoveFilesOnly,
                CopyFlags, DirectoryCopyFlags, CopyAll, IncludeSecurity, Restartable, BackupMode,
                RestartableBackupMode, CopySymlinksAsLinks, ArchiveOnly, ArchiveOnlyAndReset,
                IncludeAttributeFilter, ExcludeAttributeFilter, MinFileAge, MaxFileAge,
                MinFileSizeBytes, MaxFileSizeBytes, ExcludeOlder, ExcludeNewer, ExcludeChanged,
                ExcludeExtra, MultithreadCount, Retries, WaitSeconds, FatFileTimestampTolerance,
                AssumeFatDst, Verbose, AppendToLog, ExtraRawArguments
            ) VALUES (
                @Id, @Mirror, @CopySubdirectories, @CopyEmptySubdirectories, @Purge, @Move, @MoveFilesOnly,
                @CopyFlags, @DirectoryCopyFlags, @CopyAll, @IncludeSecurity, @Restartable, @BackupMode,
                @RestartableBackupMode, @CopySymlinksAsLinks, @ArchiveOnly, @ArchiveOnlyAndReset,
                @IncludeAttributeFilter, @ExcludeAttributeFilter, @MinFileAge, @MaxFileAge,
                @MinFileSizeBytes, @MaxFileSizeBytes, @ExcludeOlder, @ExcludeNewer, @ExcludeChanged,
                @ExcludeExtra, @MultithreadCount, @Retries, @WaitSeconds, @FatFileTimestampTolerance,
                @AssumeFatDst, @Verbose, @AppendToLog, @ExtraRawArguments
            )
            """,
            optionSet);

    public Task UpdateAsync(RobocopyOptionSet optionSet) =>
        connection.ExecuteAsync(
            """
            UPDATE RobocopyOptionSet SET
                Mirror = @Mirror, CopySubdirectories = @CopySubdirectories,
                CopyEmptySubdirectories = @CopyEmptySubdirectories, Purge = @Purge, Move = @Move,
                MoveFilesOnly = @MoveFilesOnly, CopyFlags = @CopyFlags, DirectoryCopyFlags = @DirectoryCopyFlags,
                CopyAll = @CopyAll, IncludeSecurity = @IncludeSecurity, Restartable = @Restartable,
                BackupMode = @BackupMode, RestartableBackupMode = @RestartableBackupMode,
                CopySymlinksAsLinks = @CopySymlinksAsLinks, ArchiveOnly = @ArchiveOnly,
                ArchiveOnlyAndReset = @ArchiveOnlyAndReset, IncludeAttributeFilter = @IncludeAttributeFilter,
                ExcludeAttributeFilter = @ExcludeAttributeFilter, MinFileAge = @MinFileAge,
                MaxFileAge = @MaxFileAge, MinFileSizeBytes = @MinFileSizeBytes, MaxFileSizeBytes = @MaxFileSizeBytes,
                ExcludeOlder = @ExcludeOlder, ExcludeNewer = @ExcludeNewer, ExcludeChanged = @ExcludeChanged,
                ExcludeExtra = @ExcludeExtra, MultithreadCount = @MultithreadCount, Retries = @Retries,
                WaitSeconds = @WaitSeconds, FatFileTimestampTolerance = @FatFileTimestampTolerance,
                AssumeFatDst = @AssumeFatDst, Verbose = @Verbose, AppendToLog = @AppendToLog,
                ExtraRawArguments = @ExtraRawArguments
            WHERE Id = @Id
            """,
            optionSet);

    public Task DeleteAsync(string id) =>
        connection.ExecuteAsync("DELETE FROM RobocopyOptionSet WHERE Id = @Id", new { Id = id });
}
