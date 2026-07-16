using System.Data;
using Dapper;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Repositories;

public sealed class FolderPairRunResultRepository(IDbConnection connection)
{
    public async Task<IReadOnlyList<FolderPairRunResult>> GetByRunHistoryIdAsync(int runHistoryId) =>
        (await connection.QueryAsync<FolderPairRunResult>(
            "SELECT * FROM FolderPairRunResult WHERE RunHistoryId = @RunHistoryId ORDER BY StartedUtc",
            new { RunHistoryId = runHistoryId })).AsList();

    public async Task<int> InsertAsync(FolderPairRunResult result)
    {
        var id = await connection.QuerySingleAsync<long>(
            """
            INSERT INTO FolderPairRunResult (
                RunHistoryId, FolderPairId, StartedUtc, CompletedUtc, RobocopyExitCode, RobocopyOutcomeSummary,
                DirsCopied, DirsSkipped, DirsExtras, DirsFailed, DirsMismatch,
                FilesCopied, FilesSkipped, FilesExtras, FilesFailed, FilesMismatch,
                BytesCopied, AverageSpeedBytesPerSec, RobocopyLogFilePath,
                ChecksumOutcomeSummary, ChecksumManifestPath, ChecksumReportPath, ChecksumHasIssues,
                FlacOutcomeSummary, FlacReportPath, FlacHasIssues, ErrorMessage
            ) VALUES (
                @RunHistoryId, @FolderPairId, @StartedUtc, @CompletedUtc, @RobocopyExitCode, @RobocopyOutcomeSummary,
                @DirsCopied, @DirsSkipped, @DirsExtras, @DirsFailed, @DirsMismatch,
                @FilesCopied, @FilesSkipped, @FilesExtras, @FilesFailed, @FilesMismatch,
                @BytesCopied, @AverageSpeedBytesPerSec, @RobocopyLogFilePath,
                @ChecksumOutcomeSummary, @ChecksumManifestPath, @ChecksumReportPath, @ChecksumHasIssues,
                @FlacOutcomeSummary, @FlacReportPath, @FlacHasIssues, @ErrorMessage
            );
            SELECT last_insert_rowid();
            """,
            result);
        return (int)id;
    }

    public Task UpdateAsync(FolderPairRunResult result) =>
        connection.ExecuteAsync(
            """
            UPDATE FolderPairRunResult SET
                CompletedUtc = @CompletedUtc, RobocopyExitCode = @RobocopyExitCode,
                RobocopyOutcomeSummary = @RobocopyOutcomeSummary,
                DirsCopied = @DirsCopied, DirsSkipped = @DirsSkipped, DirsExtras = @DirsExtras,
                DirsFailed = @DirsFailed, DirsMismatch = @DirsMismatch,
                FilesCopied = @FilesCopied, FilesSkipped = @FilesSkipped, FilesExtras = @FilesExtras,
                FilesFailed = @FilesFailed, FilesMismatch = @FilesMismatch,
                BytesCopied = @BytesCopied, AverageSpeedBytesPerSec = @AverageSpeedBytesPerSec,
                RobocopyLogFilePath = @RobocopyLogFilePath, ChecksumOutcomeSummary = @ChecksumOutcomeSummary,
                ChecksumManifestPath = @ChecksumManifestPath, ChecksumReportPath = @ChecksumReportPath,
                ChecksumHasIssues = @ChecksumHasIssues, FlacOutcomeSummary = @FlacOutcomeSummary,
                FlacReportPath = @FlacReportPath, FlacHasIssues = @FlacHasIssues, ErrorMessage = @ErrorMessage
            WHERE Id = @Id
            """,
            result);
}
