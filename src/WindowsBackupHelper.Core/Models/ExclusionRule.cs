namespace WindowsBackupHelper.Core.Models;

public enum ExclusionScope
{
    Global,
    Job,
    FolderPair,
}

public enum ExclusionPatternType
{
    Wildcard,
    Regex,
}

public enum ExclusionTargetType
{
    File,
    Directory,
    Both,
}

/// <summary>
/// A single exclusion rule. Rules across scopes are a union (additive) — unlike the
/// RobocopyOptionSet cascade, which overrides.
/// </summary>
public sealed class ExclusionRule
{
    public int Id { get; set; }
    public required ExclusionScope Scope { get; set; }
    public string? JobId { get; set; }
    public string? FolderPairId { get; set; }
    public required ExclusionPatternType PatternType { get; set; }
    public required string Pattern { get; set; }
    public required ExclusionTargetType TargetType { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
}
