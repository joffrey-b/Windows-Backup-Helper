namespace WindowsBackupHelper.Core.Models;

public sealed class FolderPairRunResult
{
    public int Id { get; set; }
    public required int RunHistoryId { get; set; }
    public required string FolderPairId { get; set; }
    public required DateTime StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }

    public int? RobocopyExitCode { get; set; }
    public string? RobocopyOutcomeSummary { get; set; }

    public int? DirsCopied { get; set; }
    public int? DirsSkipped { get; set; }
    public int? DirsExtras { get; set; }
    public int? DirsFailed { get; set; }
    public int? DirsMismatch { get; set; }

    public int? FilesCopied { get; set; }
    public int? FilesSkipped { get; set; }
    public int? FilesExtras { get; set; }
    public int? FilesFailed { get; set; }
    public int? FilesMismatch { get; set; }

    public long? BytesCopied { get; set; }
    public double? AverageSpeedBytesPerSec { get; set; }
    public string? RobocopyLogFilePath { get; set; }

    public string? ChecksumOutcomeSummary { get; set; }
    public string? ChecksumManifestPath { get; set; }
    public string? ChecksumReportPath { get; set; }
    public bool? ChecksumHasIssues { get; set; }
    public string? FlacOutcomeSummary { get; set; }
    public string? FlacReportPath { get; set; }
    public bool? FlacHasIssues { get; set; }
    public string? ErrorMessage { get; set; }
}
