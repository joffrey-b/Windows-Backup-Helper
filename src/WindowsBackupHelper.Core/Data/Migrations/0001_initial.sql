-- Initial schema.
--
-- Design principle: no column in this database is ever capable of holding a
-- secret. Credentials live exclusively in Windows Credential Manager;
-- CredentialTarget only stores a GUID-keyed pointer into it.

CREATE TABLE RobocopyOptionSet (
    Id                          TEXT NOT NULL PRIMARY KEY,
    Mirror                      INTEGER NULL,
    CopySubdirectories          INTEGER NULL, -- /S
    CopyEmptySubdirectories     INTEGER NULL, -- /E
    Purge                       INTEGER NULL, -- /PURGE
    Move                        INTEGER NULL, -- /MOVE
    MoveFilesOnly               INTEGER NULL, -- /MOV
    CopyFlags                   TEXT NULL,    -- /COPY:
    DirectoryCopyFlags          TEXT NULL,    -- /DCOPY:
    CopyAll                     INTEGER NULL, -- /COPYALL
    IncludeSecurity             INTEGER NULL, -- /SEC
    Restartable                 INTEGER NULL, -- /Z
    BackupMode                  INTEGER NULL, -- /B
    RestartableBackupMode       INTEGER NULL, -- /ZB
    CopySymlinksAsLinks         INTEGER NULL, -- /SL
    ArchiveOnly                 INTEGER NULL, -- /A
    ArchiveOnlyAndReset         INTEGER NULL, -- /M
    IncludeAttributeFilter      TEXT NULL,    -- /IA:
    ExcludeAttributeFilter      TEXT NULL,    -- /XA:
    MinFileAge                  TEXT NULL,    -- /MINAGE:
    MaxFileAge                  TEXT NULL,    -- /MAXAGE:
    MinFileSizeBytes            INTEGER NULL, -- /MIN:
    MaxFileSizeBytes            INTEGER NULL, -- /MAX:
    ExcludeOlder                INTEGER NULL, -- /XO
    ExcludeNewer                INTEGER NULL, -- /XN
    ExcludeChanged              INTEGER NULL, -- /XC
    ExcludeExtra                INTEGER NULL, -- /XX
    MultithreadCount            INTEGER NULL, -- /MT:
    Retries                     INTEGER NULL, -- /R:
    WaitSeconds                 INTEGER NULL, -- /W:
    FatFileTimestampTolerance   INTEGER NULL, -- /FFT
    AssumeFatDst                INTEGER NULL, -- /DST
    Verbose                     INTEGER NULL, -- /V
    AppendToLog                 INTEGER NULL, -- /LOG vs /LOG+
    ExtraRawArguments           TEXT NULL
);

CREATE TABLE AppSettings (
    Id                          INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
    FlacExecutablePath          TEXT NULL,
    DefaultRobocopyOptionSetId  TEXT NOT NULL REFERENCES RobocopyOptionSet (Id),
    NotificationsEnabled        INTEGER NOT NULL DEFAULT 1,
    DefaultChecksumWorkers      INTEGER NOT NULL DEFAULT 4,
    DefaultFlacWorkers          INTEGER NOT NULL DEFAULT 8
);

CREATE TABLE CredentialTarget (
    Id                          TEXT NOT NULL PRIMARY KEY,
    Label                       TEXT NOT NULL,
    HostOrUncRoot               TEXT NOT NULL,
    CredentialManagerTargetName TEXT NOT NULL UNIQUE
);

CREATE TABLE VerificationSettings (
    Id                          TEXT NOT NULL PRIMARY KEY,
    ChecksumMode                TEXT NOT NULL DEFAULT 'None'
                                CHECK (ChecksumMode IN ('None', 'Generate', 'VerifyAgainstManifest', 'Update')),
    ChecksumManifestPath        TEXT NULL,
    ChecksumWorkers             INTEGER NULL,
    ChecksumReportOutputPath    TEXT NULL,
    RunFlacAudit                INTEGER NOT NULL DEFAULT 0,
    FlacReportOutputPath        TEXT NULL,
    FlacErrorsOnly              INTEGER NOT NULL DEFAULT 0,
    FlacWorkers                 INTEGER NULL
);

CREATE TABLE Job (
    Id                          TEXT NOT NULL PRIMARY KEY,
    Name                        TEXT NOT NULL,
    Description                 TEXT NULL,
    JobRobocopyOptionSetId      TEXT NULL REFERENCES RobocopyOptionSet (Id),
    IsEnabled                   INTEGER NOT NULL DEFAULT 1,
    SortOrder                   INTEGER NOT NULL DEFAULT 0,
    CreatedUtc                  TEXT NOT NULL,
    UpdatedUtc                  TEXT NOT NULL,
    -- Deleting a job with existing run history can't hard-delete the row (RunHistory.JobId
    -- references it), so "delete" just hides it from the active Jobs list instead, preserving
    -- the audit trail (see the "Job referenced" column in Run History).
    IsDeleted                   INTEGER NOT NULL DEFAULT 0
);

-- Name only needs to be unique among active jobs -- once a job is soft-deleted, its name frees
-- up for reuse (e.g. by a newly-added job) rather than being reserved forever.
CREATE UNIQUE INDEX UX_Job_Name_WhenActive ON Job (Name) WHERE IsDeleted = 0;

CREATE TABLE FolderPair (
    Id                              TEXT NOT NULL PRIMARY KEY,
    JobId                           TEXT NOT NULL REFERENCES Job (Id) ON DELETE CASCADE,
    Name                            TEXT NULL,
    SourcePath                      TEXT NOT NULL,
    DestinationPath                 TEXT NOT NULL,
    SourceCredentialTargetId        TEXT NULL REFERENCES CredentialTarget (Id),
    DestinationCredentialTargetId   TEXT NULL REFERENCES CredentialTarget (Id),
    PairRobocopyOptionSetId         TEXT NULL REFERENCES RobocopyOptionSet (Id),
    VerificationSettingsId          TEXT NULL REFERENCES VerificationSettings (Id),
    SortOrder                       INTEGER NOT NULL DEFAULT 0,
    IsEnabled                       INTEGER NOT NULL DEFAULT 1,
    -- Same soft-delete reasoning as Job.IsDeleted, for FolderPairRunResult.FolderPairId.
    IsDeleted                       INTEGER NOT NULL DEFAULT 0
);

CREATE TABLE ExclusionRule (
    Id              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    Scope           TEXT NOT NULL CHECK (Scope IN ('Global', 'Job', 'FolderPair')),
    JobId           TEXT NULL REFERENCES Job (Id) ON DELETE CASCADE,
    FolderPairId    TEXT NULL REFERENCES FolderPair (Id) ON DELETE CASCADE,
    PatternType     TEXT NOT NULL CHECK (PatternType IN ('Wildcard', 'Regex')),
    Pattern         TEXT NOT NULL,
    TargetType      TEXT NOT NULL CHECK (TargetType IN ('File', 'Directory', 'Both')),
    IsEnabled       INTEGER NOT NULL DEFAULT 1,
    Description     TEXT NULL,
    SortOrder       INTEGER NOT NULL DEFAULT 0,
    CHECK (
        (Scope = 'Global'     AND JobId IS NULL     AND FolderPairId IS NULL) OR
        (Scope = 'Job'        AND JobId IS NOT NULL AND FolderPairId IS NULL) OR
        (Scope = 'FolderPair' AND JobId IS NULL     AND FolderPairId IS NOT NULL)
    )
);

CREATE TABLE RunHistory (
    Id              INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    JobId           TEXT NOT NULL REFERENCES Job (Id),
    TriggerType     TEXT NOT NULL CHECK (TriggerType IN ('Manual', 'Scheduled', 'Cli')),
    StartedUtc      TEXT NOT NULL,
    CompletedUtc    TEXT NULL,
    WasDryRun       INTEGER NOT NULL DEFAULT 0,
    OverallOutcome  TEXT NULL CHECK (OverallOutcome IN ('Success', 'SuccessWithMismatches', 'Failed', 'Cancelled', 'PartialFailure')),
    Notes           TEXT NULL
);

CREATE TABLE FolderPairRunResult (
    Id                          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    RunHistoryId                INTEGER NOT NULL REFERENCES RunHistory (Id) ON DELETE CASCADE,
    FolderPairId                TEXT NOT NULL REFERENCES FolderPair (Id),
    StartedUtc                  TEXT NOT NULL,
    CompletedUtc                TEXT NULL,
    RobocopyExitCode            INTEGER NULL,
    RobocopyOutcomeSummary      TEXT NULL,
    DirsCopied                  INTEGER NULL,
    DirsSkipped                 INTEGER NULL,
    DirsExtras                  INTEGER NULL,
    DirsFailed                  INTEGER NULL,
    DirsMismatch                INTEGER NULL,
    FilesCopied                 INTEGER NULL,
    FilesSkipped                INTEGER NULL,
    FilesExtras                 INTEGER NULL,
    FilesFailed                 INTEGER NULL,
    FilesMismatch               INTEGER NULL,
    BytesCopied                 INTEGER NULL,
    AverageSpeedBytesPerSec     REAL NULL,
    RobocopyLogFilePath         TEXT NULL,
    ChecksumOutcomeSummary      TEXT NULL,
    ChecksumManifestPath        TEXT NULL,
    ChecksumReportPath          TEXT NULL,
    ChecksumHasIssues           INTEGER NULL,
    FlacOutcomeSummary          TEXT NULL,
    FlacReportPath              TEXT NULL,
    FlacHasIssues               INTEGER NULL,
    ErrorMessage                TEXT NULL
);

CREATE TABLE ScheduleMetadata (
    Id                          INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    JobId                       TEXT NOT NULL REFERENCES Job (Id) ON DELETE CASCADE,
    TaskSchedulerTaskName       TEXT NOT NULL UNIQUE,
    TriggerDescription          TEXT NULL,
    IsEnabled                   INTEGER NOT NULL DEFAULT 1,
    RunWhetherUserLoggedOnOrNot INTEGER NOT NULL DEFAULT 0,
    LastSyncedUtc               TEXT NULL
);

CREATE INDEX IX_FolderPair_JobId ON FolderPair (JobId);
CREATE INDEX IX_ExclusionRule_JobId ON ExclusionRule (JobId);
CREATE INDEX IX_ExclusionRule_FolderPairId ON ExclusionRule (FolderPairId);
CREATE INDEX IX_RunHistory_JobId ON RunHistory (JobId);
CREATE INDEX IX_FolderPairRunResult_RunHistoryId ON FolderPairRunResult (RunHistoryId);
CREATE INDEX IX_FolderPairRunResult_FolderPairId ON FolderPairRunResult (FolderPairId);
CREATE INDEX IX_ScheduleMetadata_JobId ON ScheduleMetadata (JobId);
