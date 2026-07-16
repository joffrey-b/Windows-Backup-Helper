namespace WindowsBackupHelper.Core.Models;

public enum ChecksumMode
{
    None,
    Generate,
    VerifyAgainstManifest,
    Update,
}

public sealed class VerificationSettings
{
    public required string Id { get; init; }
    public ChecksumMode ChecksumMode { get; set; } = ChecksumMode.None;
    public string? ChecksumManifestPath { get; set; }
    public int? ChecksumWorkers { get; set; }
    public string? ChecksumReportOutputPath { get; set; }
    public bool RunFlacAudit { get; set; }
    public string? FlacReportOutputPath { get; set; }
    public bool FlacErrorsOnly { get; set; }
    public int? FlacWorkers { get; set; }
}
