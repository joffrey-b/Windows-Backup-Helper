namespace WindowsBackupHelper.Core.Models;

public sealed class Job
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public string? JobRobocopyOptionSetId { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int SortOrder { get; set; }
    public required DateTime CreatedUtc { get; set; }
    public required DateTime UpdatedUtc { get; set; }

    /// <summary>Soft-delete flag — "deleting" a job with run history can't hard-delete the row
    /// (RunHistory.JobId references it), so it's hidden from active lists instead.</summary>
    public bool IsDeleted { get; set; }
}
