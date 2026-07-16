using WindowsBackupHelper.Core.Exclusions;

namespace WindowsBackupHelper.Core.Tests.Exclusions;

/// <summary>A synthetic file tree for exercising ExclusionRuleResolver without touching disk.</summary>
public sealed class FakeFileSystemEnumerator(params FileSystemEntry[] entries) : IFileSystemEnumerator
{
    private readonly IReadOnlyList<FileSystemEntry> _entries = entries;

    public IEnumerable<FileSystemEntry> Enumerate(string rootPath) => _entries;
}
