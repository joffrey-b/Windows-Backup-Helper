using WindowsBackupHelper.Core.Exclusions;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Tests.Exclusions;

public sealed class ExclusionRuleResolverTests
{
    private static ExclusionRule Rule(
        ExclusionPatternType patternType, string pattern, ExclusionTargetType targetType = ExclusionTargetType.File, bool isEnabled = true) => new()
    {
        Scope = ExclusionScope.Global,
        PatternType = patternType,
        Pattern = pattern,
        TargetType = targetType,
        IsEnabled = isEnabled,
    };

    [Fact]
    public void Resolve_BareNameWildcard_CollapsesToASingleTokenWithoutTouchingTheEnumerator()
    {
        var enumerator = new FakeFileSystemEnumerator(); // empty — must never be consulted
        var resolver = new ExclusionRuleResolver(enumerator);

        var result = resolver.Resolve([Rule(ExclusionPatternType.Wildcard, "*.tmp")], "/root");

        Assert.Single(result.ExcludeFileTokens);
        Assert.Equal("*.tmp", result.ExcludeFileTokens[0]);
    }

    [Fact]
    public void Resolve_BothTargetType_Collapsible_AddsToBothFileAndDirectoryTokenLists()
    {
        var resolver = new ExclusionRuleResolver(new FakeFileSystemEnumerator());

        var result = resolver.Resolve([Rule(ExclusionPatternType.Wildcard, "Thumbs.db", ExclusionTargetType.Both)], "/root");

        Assert.Contains("Thumbs.db", result.ExcludeFileTokens);
        Assert.Contains("Thumbs.db", result.ExcludeDirectoryTokens);
    }

    [Fact]
    public void Resolve_PathScopedWildcard_RequiresEnumerationAndResolvesToConcretePaths()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("Artist/Album/Disc 1/track01.flac", false),
            new FileSystemEntry("Artist/Album/Disc 1/track02.flac", false),
            new FileSystemEntry("Artist/Album/cover.jpg", false));
        var resolver = new ExclusionRuleResolver(enumerator);

        var result = resolver.Resolve([Rule(ExclusionPatternType.Wildcard, "Artist/Album/Disc 1/*.flac")], "/root");

        Assert.Equal(2, result.ExcludeFileTokens.Count);
        Assert.Contains("Artist/Album/Disc 1/track01.flac", result.ExcludeFileTokens);
        Assert.Contains("Artist/Album/Disc 1/track02.flac", result.ExcludeFileTokens);
        Assert.DoesNotContain("Artist/Album/cover.jpg", result.ExcludeFileTokens);
    }

    [Fact]
    public void Resolve_RegexRule_MatchesAgainstRelativePaths()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("Music/Bootlegs/live_show.flac", false),
            new FileSystemEntry("Music/Studio/track.flac", false));
        var resolver = new ExclusionRuleResolver(enumerator);

        var result = resolver.Resolve([Rule(ExclusionPatternType.Regex, "^Music/Bootlegs/", ExclusionTargetType.File)], "/root");

        Assert.Single(result.ExcludeFileTokens);
        Assert.Equal("Music/Bootlegs/live_show.flac", result.ExcludeFileTokens[0]);
    }

    [Fact]
    public void Resolve_RegexRule_IsCaseInsensitive()
    {
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("ARTIST/ALBUM/TRACK.FLAC", false));
        var resolver = new ExclusionRuleResolver(enumerator);

        var result = resolver.Resolve([Rule(ExclusionPatternType.Regex, "^artist/album/track\\.flac$")], "/root");

        Assert.Single(result.ExcludeFileTokens);
    }

    [Fact]
    public void Resolve_TargetTypeDirectory_OnlyMatchesDirectoryEntries()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("Artist/@eaDir", true),
            new FileSystemEntry("Artist/@eaDir_notes.txt", false)); // a file that happens to share the prefix
        var resolver = new ExclusionRuleResolver(enumerator);

        var result = resolver.Resolve([Rule(ExclusionPatternType.Regex, "@eaDir$", ExclusionTargetType.Directory)], "/root");

        Assert.Single(result.ExcludeDirectoryTokens);
        Assert.Empty(result.ExcludeFileTokens);
    }

    [Fact]
    public void Resolve_BothTargetType_WithEnumeration_RoutesMatchesByActualEntryType()
    {
        var enumerator = new FakeFileSystemEnumerator(
            new FileSystemEntry("Artist/cache", true),
            new FileSystemEntry("Artist/cache.bak", false));
        var resolver = new ExclusionRuleResolver(enumerator);

        var result = resolver.Resolve([Rule(ExclusionPatternType.Regex, "^Artist/cache", ExclusionTargetType.Both)], "/root");

        Assert.Contains("Artist/cache", result.ExcludeDirectoryTokens);
        Assert.Contains("Artist/cache.bak", result.ExcludeFileTokens);
    }

    [Fact]
    public void Resolve_DisabledRule_IsIgnoredEntirely()
    {
        var resolver = new ExclusionRuleResolver(new FakeFileSystemEnumerator());

        var result = resolver.Resolve([Rule(ExclusionPatternType.Wildcard, "*.tmp", isEnabled: false)], "/root");

        Assert.Empty(result.ExcludeFileTokens);
    }

    [Fact]
    public void Resolve_UnionOfMultipleRules_CollapsibleAndPathScoped_BothContribute()
    {
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("Sub/only-here.log", false));
        var resolver = new ExclusionRuleResolver(enumerator);

        var result = resolver.Resolve(
            [
                Rule(ExclusionPatternType.Wildcard, "*.tmp"),
                Rule(ExclusionPatternType.Wildcard, "Sub/*.log"),
            ],
            "/root");

        Assert.Contains("*.tmp", result.ExcludeFileTokens);
        Assert.Contains("Sub/only-here.log", result.ExcludeFileTokens);
    }

    [Fact]
    public void Resolve_NoMatches_ReturnsEmptyListsWithoutWarning()
    {
        var enumerator = new FakeFileSystemEnumerator(new FileSystemEntry("Artist/track.flac", false));
        var resolver = new ExclusionRuleResolver(enumerator);

        var result = resolver.Resolve([Rule(ExclusionPatternType.Regex, "^NoSuchPath/")], "/root");

        Assert.Empty(result.ExcludeFileTokens);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Resolve_LargeResolvedArgumentLength_ProducesAWarning()
    {
        var manyEntries = Enumerable.Range(0, 2000)
            .Select(i => new FileSystemEntry($"Artist/Album/VeryLongTrackNameForPaddingPurposes_{i:D4}.flac", false))
            .ToArray();
        var enumerator = new FakeFileSystemEnumerator(manyEntries);
        var resolver = new ExclusionRuleResolver(enumerator);

        var result = resolver.Resolve([Rule(ExclusionPatternType.Regex, "^Artist/Album/.*\\.flac$")], "/root");

        Assert.Equal(2000, result.ExcludeFileTokens.Count);
        Assert.Contains(result.Warnings, w => w.Contains("32,767", StringComparison.Ordinal) || w.Contains("command-line", StringComparison.OrdinalIgnoreCase));
    }
}
