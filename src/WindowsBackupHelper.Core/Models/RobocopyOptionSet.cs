namespace WindowsBackupHelper.Core.Models;

/// <summary>
/// A set of Robocopy option overrides. Every column except <see cref="Id"/> is nullable —
/// null means "inherit from the next level down" when resolved via
/// RobocopyOptionsResolver.Resolve(appDefaults, jobOverrides, pairOverrides).
/// </summary>
public sealed class RobocopyOptionSet
{
    public required string Id { get; init; }

    public bool? Mirror { get; set; }
    public bool? CopySubdirectories { get; set; } // /S
    public bool? CopyEmptySubdirectories { get; set; } // /E
    public bool? Purge { get; set; } // /PURGE
    public bool? Move { get; set; } // /MOVE
    public bool? MoveFilesOnly { get; set; } // /MOV
    public string? CopyFlags { get; set; } // /COPY:
    public string? DirectoryCopyFlags { get; set; } // /DCOPY:
    public bool? CopyAll { get; set; } // /COPYALL
    public bool? IncludeSecurity { get; set; } // /SEC
    public bool? Restartable { get; set; } // /Z
    public bool? BackupMode { get; set; } // /B
    public bool? RestartableBackupMode { get; set; } // /ZB
    public bool? CopySymlinksAsLinks { get; set; } // /SL
    public bool? ArchiveOnly { get; set; } // /A
    public bool? ArchiveOnlyAndReset { get; set; } // /M
    public string? IncludeAttributeFilter { get; set; } // /IA:
    public string? ExcludeAttributeFilter { get; set; } // /XA:
    public string? MinFileAge { get; set; } // /MINAGE:
    public string? MaxFileAge { get; set; } // /MAXAGE:
    public long? MinFileSizeBytes { get; set; } // /MIN:
    public long? MaxFileSizeBytes { get; set; } // /MAX:
    public bool? ExcludeOlder { get; set; } // /XO
    public bool? ExcludeNewer { get; set; } // /XN
    public bool? ExcludeChanged { get; set; } // /XC
    public bool? ExcludeExtra { get; set; } // /XX
    public int? MultithreadCount { get; set; } // /MT:
    public int? Retries { get; set; } // /R:
    public int? WaitSeconds { get; set; } // /W:
    public bool? FatFileTimestampTolerance { get; set; } // /FFT
    public bool? AssumeFatDst { get; set; } // /DST
    public bool? Verbose { get; set; } // /V
    public bool? AppendToLog { get; set; } // /LOG vs /LOG+
    public string? ExtraRawArguments { get; set; }
}
