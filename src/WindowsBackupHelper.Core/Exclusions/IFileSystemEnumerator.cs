namespace WindowsBackupHelper.Core.Exclusions;

/// <summary>A single file or directory found under an enumeration root.</summary>
/// <param name="RelativePath">Forward-slash normalized, relative to the enumeration root (same convention as the checksum manifest).</param>
/// <param name="IsDirectory">Whether this entry is a directory rather than a file.</param>
public readonly record struct FileSystemEntry(string RelativePath, bool IsDirectory);

/// <summary>
/// Abstracts filesystem enumeration so ExclusionRuleResolver's matching/collapsing logic is
/// unit-testable against synthetic path lists, independent of any real file tree.
/// </summary>
public interface IFileSystemEnumerator
{
    IEnumerable<FileSystemEntry> Enumerate(string rootPath);
}
