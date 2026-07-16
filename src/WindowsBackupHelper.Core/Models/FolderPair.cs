namespace WindowsBackupHelper.Core.Models;

/// <summary>
/// One source -> destination pair within a Job. Source and destination each carry their
/// own nullable credential reference since a pair can span two different NAS hosts.
/// </summary>
public sealed class FolderPair
{
    public required string Id { get; init; }
    public required string JobId { get; set; }
    public string? Name { get; set; }
    public required string SourcePath { get; set; }
    public required string DestinationPath { get; set; }
    public string? SourceCredentialTargetId { get; set; }
    public string? DestinationCredentialTargetId { get; set; }
    public string? PairRobocopyOptionSetId { get; set; }
    public string? VerificationSettingsId { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>Soft-delete flag — "deleting" a pair with run history can't hard-delete the
    /// row (FolderPairRunResult.FolderPairId references it), so it's hidden from the active
    /// Folder Pairs grid instead.</summary>
    public bool IsDeleted { get; set; }
}
