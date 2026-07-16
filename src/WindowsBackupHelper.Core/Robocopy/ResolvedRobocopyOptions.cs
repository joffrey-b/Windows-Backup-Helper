namespace WindowsBackupHelper.Core.Robocopy;

/// <summary>
/// The fully-cascaded, ready-to-build result of RobocopyOptionsResolver.Resolve. Unlike
/// RobocopyOptionSet (the nullable, per-level DB row), every flag here is a concrete bool —
/// Robocopy's CLI has no way to explicitly negate a flag, so "unset" and "false" collapse
/// to the same thing ("don't emit this flag") once resolution is done. Retries and
/// WaitSeconds are non-nullable by construction: Robocopy's own defaults
/// (1,000,000 retries x 30s) are effectively "hang forever", so no code path may build
/// arguments without these resolved.
/// </summary>
public sealed record ResolvedRobocopyOptions
{
    public bool Mirror { get; init; }
    public bool CopySubdirectories { get; init; } // /S
    public bool CopyEmptySubdirectories { get; init; } // /E
    public bool Purge { get; init; } // /PURGE
    public bool Move { get; init; } // /MOVE
    public bool MoveFilesOnly { get; init; } // /MOV
    public string? CopyFlags { get; init; } // /COPY:
    public string? DirectoryCopyFlags { get; init; } // /DCOPY:
    public bool CopyAll { get; init; } // /COPYALL
    public bool IncludeSecurity { get; init; } // /SEC
    public bool Restartable { get; init; } // /Z
    public bool BackupMode { get; init; } // /B
    public bool RestartableBackupMode { get; init; } // /ZB
    public bool CopySymlinksAsLinks { get; init; } // /SL
    public bool ArchiveOnly { get; init; } // /A
    public bool ArchiveOnlyAndReset { get; init; } // /M
    public string? IncludeAttributeFilter { get; init; } // /IA:
    public string? ExcludeAttributeFilter { get; init; } // /XA:
    public string? MinFileAge { get; init; } // /MINAGE:
    public string? MaxFileAge { get; init; } // /MAXAGE:
    public long? MinFileSizeBytes { get; init; } // /MIN:
    public long? MaxFileSizeBytes { get; init; } // /MAX:
    public bool ExcludeOlder { get; init; } // /XO
    public bool ExcludeNewer { get; init; } // /XN
    public bool ExcludeChanged { get; init; } // /XC
    public bool ExcludeExtra { get; init; } // /XX
    public int? MultithreadCount { get; init; } // /MT:
    public required int Retries { get; init; } // /R:
    public required int WaitSeconds { get; init; } // /W:
    public bool FatFileTimestampTolerance { get; init; } // /FFT
    public bool AssumeFatDst { get; init; } // /DST
    public bool Verbose { get; init; } // /V
    public bool AppendToLog { get; init; } // selects /UNILOG+ instead of /UNILOG
    public string? ExtraRawArguments { get; init; }
}
