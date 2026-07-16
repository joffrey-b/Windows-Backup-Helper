using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Robocopy;

/// <summary>
/// Cascades RobocopyOptionSet rows from three levels (app defaults -> job overrides ->
/// folder-pair overrides) into one ResolvedRobocopyOptions. Every field is last-non-null-wins
/// (pair beats job beats app default) except ExtraRawArguments, which concatenates across all
/// three levels since it's additive passthrough flags, not a mutually-exclusive setting.
/// </summary>
public static class RobocopyOptionsResolver
{
    /// <summary>
    /// Robocopy's own defaults are /R:1000000 /W:30 — effectively "hang forever" on one
    /// unreachable file. These are the absolute last-resort fallbacks if even AppSettings'
    /// option set somehow leaves Retries/WaitSeconds null.
    /// </summary>
    public const int FallbackRetries = 3;

    public const int FallbackWaitSeconds = 5;

    public static ResolvedRobocopyOptions Resolve(
        RobocopyOptionSet appDefaults,
        RobocopyOptionSet? jobOverrides = null,
        RobocopyOptionSet? pairOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(appDefaults);

        return new ResolvedRobocopyOptions
        {
            Mirror = pairOverrides?.Mirror ?? jobOverrides?.Mirror ?? appDefaults.Mirror ?? false,
            CopySubdirectories = pairOverrides?.CopySubdirectories ?? jobOverrides?.CopySubdirectories ?? appDefaults.CopySubdirectories ?? false,
            CopyEmptySubdirectories = pairOverrides?.CopyEmptySubdirectories ?? jobOverrides?.CopyEmptySubdirectories ?? appDefaults.CopyEmptySubdirectories ?? false,
            Purge = pairOverrides?.Purge ?? jobOverrides?.Purge ?? appDefaults.Purge ?? false,
            Move = pairOverrides?.Move ?? jobOverrides?.Move ?? appDefaults.Move ?? false,
            MoveFilesOnly = pairOverrides?.MoveFilesOnly ?? jobOverrides?.MoveFilesOnly ?? appDefaults.MoveFilesOnly ?? false,
            CopyFlags = pairOverrides?.CopyFlags ?? jobOverrides?.CopyFlags ?? appDefaults.CopyFlags,
            DirectoryCopyFlags = pairOverrides?.DirectoryCopyFlags ?? jobOverrides?.DirectoryCopyFlags ?? appDefaults.DirectoryCopyFlags,
            CopyAll = pairOverrides?.CopyAll ?? jobOverrides?.CopyAll ?? appDefaults.CopyAll ?? false,
            IncludeSecurity = pairOverrides?.IncludeSecurity ?? jobOverrides?.IncludeSecurity ?? appDefaults.IncludeSecurity ?? false,
            Restartable = pairOverrides?.Restartable ?? jobOverrides?.Restartable ?? appDefaults.Restartable ?? false,
            BackupMode = pairOverrides?.BackupMode ?? jobOverrides?.BackupMode ?? appDefaults.BackupMode ?? false,
            RestartableBackupMode = pairOverrides?.RestartableBackupMode ?? jobOverrides?.RestartableBackupMode ?? appDefaults.RestartableBackupMode ?? false,
            CopySymlinksAsLinks = pairOverrides?.CopySymlinksAsLinks ?? jobOverrides?.CopySymlinksAsLinks ?? appDefaults.CopySymlinksAsLinks ?? false,
            ArchiveOnly = pairOverrides?.ArchiveOnly ?? jobOverrides?.ArchiveOnly ?? appDefaults.ArchiveOnly ?? false,
            ArchiveOnlyAndReset = pairOverrides?.ArchiveOnlyAndReset ?? jobOverrides?.ArchiveOnlyAndReset ?? appDefaults.ArchiveOnlyAndReset ?? false,
            IncludeAttributeFilter = pairOverrides?.IncludeAttributeFilter ?? jobOverrides?.IncludeAttributeFilter ?? appDefaults.IncludeAttributeFilter,
            ExcludeAttributeFilter = pairOverrides?.ExcludeAttributeFilter ?? jobOverrides?.ExcludeAttributeFilter ?? appDefaults.ExcludeAttributeFilter,
            MinFileAge = pairOverrides?.MinFileAge ?? jobOverrides?.MinFileAge ?? appDefaults.MinFileAge,
            MaxFileAge = pairOverrides?.MaxFileAge ?? jobOverrides?.MaxFileAge ?? appDefaults.MaxFileAge,
            MinFileSizeBytes = pairOverrides?.MinFileSizeBytes ?? jobOverrides?.MinFileSizeBytes ?? appDefaults.MinFileSizeBytes,
            MaxFileSizeBytes = pairOverrides?.MaxFileSizeBytes ?? jobOverrides?.MaxFileSizeBytes ?? appDefaults.MaxFileSizeBytes,
            ExcludeOlder = pairOverrides?.ExcludeOlder ?? jobOverrides?.ExcludeOlder ?? appDefaults.ExcludeOlder ?? false,
            ExcludeNewer = pairOverrides?.ExcludeNewer ?? jobOverrides?.ExcludeNewer ?? appDefaults.ExcludeNewer ?? false,
            ExcludeChanged = pairOverrides?.ExcludeChanged ?? jobOverrides?.ExcludeChanged ?? appDefaults.ExcludeChanged ?? false,
            ExcludeExtra = pairOverrides?.ExcludeExtra ?? jobOverrides?.ExcludeExtra ?? appDefaults.ExcludeExtra ?? false,
            MultithreadCount = pairOverrides?.MultithreadCount ?? jobOverrides?.MultithreadCount ?? appDefaults.MultithreadCount,
            Retries = pairOverrides?.Retries ?? jobOverrides?.Retries ?? appDefaults.Retries ?? FallbackRetries,
            WaitSeconds = pairOverrides?.WaitSeconds ?? jobOverrides?.WaitSeconds ?? appDefaults.WaitSeconds ?? FallbackWaitSeconds,
            FatFileTimestampTolerance = pairOverrides?.FatFileTimestampTolerance ?? jobOverrides?.FatFileTimestampTolerance ?? appDefaults.FatFileTimestampTolerance ?? false,
            AssumeFatDst = pairOverrides?.AssumeFatDst ?? jobOverrides?.AssumeFatDst ?? appDefaults.AssumeFatDst ?? false,
            Verbose = pairOverrides?.Verbose ?? jobOverrides?.Verbose ?? appDefaults.Verbose ?? false,
            AppendToLog = pairOverrides?.AppendToLog ?? jobOverrides?.AppendToLog ?? appDefaults.AppendToLog ?? false,
            ExtraRawArguments = string.Join(
                ' ',
                new[] { appDefaults.ExtraRawArguments, jobOverrides?.ExtraRawArguments, pairOverrides?.ExtraRawArguments }
                    .Where(s => !string.IsNullOrWhiteSpace(s))),
        };
    }

    /// <summary>
    /// Creates a new override RobocopyOptionSet with its checkboxes pre-populated to match
    /// what's currently effective at the level(s) below — so a freshly-enabled override starts
    /// as an honest, fully-concrete on/off snapshot instead of a wall of nulls. Only the boolean
    /// (checkbox-backed) fields are materialized; numeric/string fields are left null so their
    /// text boxes still show blank ("not set here, inherits"), which isn't ambiguous the way a
    /// tri-state checkbox is. ExtraRawArguments is deliberately left null too, since the resolver
    /// concatenates it additively across levels — copying the already-resolved text down would
    /// double it up the next time options are resolved.
    /// </summary>
    public static RobocopyOptionSet CreateMaterializedOverride(string id, RobocopyOptionSet appDefaults, RobocopyOptionSet? jobOverrides = null)
    {
        var materialized = new RobocopyOptionSet { Id = id };
        BackfillNullBooleans(materialized, appDefaults, jobOverrides);
        return materialized;
    }

    /// <summary>
    /// Fills in only the still-null boolean (checkbox-backed) fields of <paramref name="target"/>
    /// with what's currently effective at the level(s) below, leaving any field the caller
    /// already explicitly set (true or false) untouched. Used both to materialize a brand-new
    /// override (<see cref="CreateMaterializedOverride"/>) and to backfill a legacy row loaded
    /// from before this method existed, whose untouched fields would otherwise stay null and
    /// render as an ambiguous indeterminate checkbox even though IsThreeState is off.
    /// </summary>
    public static void BackfillNullBooleans(RobocopyOptionSet target, RobocopyOptionSet appDefaults, RobocopyOptionSet? jobOverrides = null)
    {
        var resolved = Resolve(appDefaults, jobOverrides);

        target.Mirror ??= resolved.Mirror;
        target.CopySubdirectories ??= resolved.CopySubdirectories;
        target.CopyEmptySubdirectories ??= resolved.CopyEmptySubdirectories;
        target.Purge ??= resolved.Purge;
        target.Move ??= resolved.Move;
        target.MoveFilesOnly ??= resolved.MoveFilesOnly;
        target.CopyAll ??= resolved.CopyAll;
        target.IncludeSecurity ??= resolved.IncludeSecurity;
        target.Restartable ??= resolved.Restartable;
        target.BackupMode ??= resolved.BackupMode;
        target.RestartableBackupMode ??= resolved.RestartableBackupMode;
        target.CopySymlinksAsLinks ??= resolved.CopySymlinksAsLinks;
        target.ArchiveOnly ??= resolved.ArchiveOnly;
        target.ArchiveOnlyAndReset ??= resolved.ArchiveOnlyAndReset;
        target.ExcludeOlder ??= resolved.ExcludeOlder;
        target.ExcludeNewer ??= resolved.ExcludeNewer;
        target.ExcludeChanged ??= resolved.ExcludeChanged;
        target.ExcludeExtra ??= resolved.ExcludeExtra;
        target.FatFileTimestampTolerance ??= resolved.FatFileTimestampTolerance;
        target.AssumeFatDst ??= resolved.AssumeFatDst;
        target.Verbose ??= resolved.Verbose;
        target.AppendToLog ??= resolved.AppendToLog;
    }
}
