namespace WindowsBackupHelper.Core.Exclusions;

/// <summary>
/// Real, recursive filesystem enumeration via System.IO. Not Windows-only — Directory.Enumerate*
/// is plain BCL — so this stays in Core alongside the fake used by tests.
/// </summary>
public sealed class DirectoryFileSystemEnumerator : IFileSystemEnumerator
{
    public IEnumerable<FileSystemEntry> Enumerate(string rootPath)
    {
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var path in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
            yield return new FileSystemEntry(relative, Directory.Exists(path));
        }
    }
}
