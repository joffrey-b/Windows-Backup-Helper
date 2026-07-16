namespace WindowsBackupHelper.Core.Models;

/// <summary>
/// Singleton row (Id is always 1). This is the one place in the RobocopyOptionSet
/// cascade guaranteed to resolve Retries/WaitSeconds to non-null values, since
/// Robocopy's own defaults (1,000,000 retries x 30s) are effectively "hang forever".
/// </summary>
public sealed class AppSettings
{
    public const int SingletonId = 1;

    public int Id { get; init; } = SingletonId;
    public string? FlacExecutablePath { get; set; }
    public required string DefaultRobocopyOptionSetId { get; set; }
    public bool NotificationsEnabled { get; set; } = true;
    public int DefaultChecksumWorkers { get; set; } = 4;
    public int DefaultFlacWorkers { get; set; } = 8;
}
