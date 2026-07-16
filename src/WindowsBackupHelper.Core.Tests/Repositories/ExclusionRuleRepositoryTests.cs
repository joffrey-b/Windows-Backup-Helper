using Dapper;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Tests.Data;

namespace WindowsBackupHelper.Core.Tests.Repositories;

public sealed class ExclusionRuleRepositoryTests : IDisposable
{
    private readonly SqliteTestDatabase _db = new();
    private readonly string _jobId = Guid.NewGuid().ToString();
    private readonly string _folderPairId = Guid.NewGuid().ToString();

    public ExclusionRuleRepositoryTests()
    {
        var job = new Job
        {
            Id = _jobId, Name = "Job", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow,
        };
        _db.Connection.Execute("INSERT INTO Job (Id, Name, CreatedUtc, UpdatedUtc) VALUES (@Id, @Name, @CreatedUtc, @UpdatedUtc)", job);
        _db.Connection.Execute(
            "INSERT INTO FolderPair (Id, JobId, SourcePath, DestinationPath) VALUES (@Id, @JobId, 'S', 'D')",
            new { Id = _folderPairId, JobId = _jobId });
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetApplicableRules_ReturnsUnionOfGlobalJobAndFolderPairScopes()
    {
        var repo = new ExclusionRuleRepository(_db.Connection);

        var globalRule = await repo.InsertAsync(new ExclusionRule
        {
            Scope = ExclusionScope.Global, PatternType = ExclusionPatternType.Wildcard,
            Pattern = "*.tmp", TargetType = ExclusionTargetType.File,
        });
        var jobRule = await repo.InsertAsync(new ExclusionRule
        {
            Scope = ExclusionScope.Job, JobId = _jobId, PatternType = ExclusionPatternType.Wildcard,
            Pattern = "Thumbs.db", TargetType = ExclusionTargetType.File,
        });
        var pairRule = await repo.InsertAsync(new ExclusionRule
        {
            Scope = ExclusionScope.FolderPair, FolderPairId = _folderPairId, PatternType = ExclusionPatternType.Regex,
            Pattern = @"^Disc\d+/", TargetType = ExclusionTargetType.Directory,
        });
        // A rule scoped to some other job/pair must not leak in.
        var otherJobId = Guid.NewGuid().ToString();
        _db.Connection.Execute(
            "INSERT INTO Job (Id, Name, CreatedUtc, UpdatedUtc) VALUES (@Id, 'Other', @Now, @Now)",
            new { Id = otherJobId, Now = DateTime.UtcNow });
        await repo.InsertAsync(new ExclusionRule
        {
            Scope = ExclusionScope.Job, JobId = otherJobId, PatternType = ExclusionPatternType.Wildcard,
            Pattern = "*.bak", TargetType = ExclusionTargetType.File,
        });

        var applicable = await repo.GetApplicableRulesAsync(_jobId, _folderPairId);

        Assert.Equal(3, applicable.Count);
        Assert.Contains(applicable, r => r.Id == globalRule);
        Assert.Contains(applicable, r => r.Id == jobRule);
        Assert.Contains(applicable, r => r.Id == pairRule);
    }

    [Fact]
    public async Task DeletingFolderPair_CascadesToItsScopedExclusionRules()
    {
        var repo = new ExclusionRuleRepository(_db.Connection);
        await repo.InsertAsync(new ExclusionRule
        {
            Scope = ExclusionScope.FolderPair, FolderPairId = _folderPairId, PatternType = ExclusionPatternType.Wildcard,
            Pattern = "*.log", TargetType = ExclusionTargetType.File,
        });

        _db.Connection.Execute("DELETE FROM FolderPair WHERE Id = @Id", new { Id = _folderPairId });

        Assert.Empty(await repo.GetByFolderPairIdAsync(_folderPairId));
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        var repo = new ExclusionRuleRepository(_db.Connection);
        var id = await repo.InsertAsync(new ExclusionRule
        {
            Scope = ExclusionScope.Global, PatternType = ExclusionPatternType.Wildcard,
            Pattern = "*.tmp", TargetType = ExclusionTargetType.File,
        });

        var rule = await repo.GetByIdAsync(id);
        rule!.IsEnabled = false;
        rule.Pattern = "*.temp";
        await repo.UpdateAsync(rule);

        var fetched = await repo.GetByIdAsync(id);
        Assert.False(fetched!.IsEnabled);
        Assert.Equal("*.temp", fetched.Pattern);
    }
}
