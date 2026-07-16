using WindowsBackupHelper.Core.Exclusions;

namespace WindowsBackupHelper.Core.Checksums;

/// <summary>
/// Finds every file worth checksumming under a root, matching
/// checksums_windows_linux.py's find_all_files: skips junk by exact name
/// (.DS_Store, Thumbs.db, desktop.ini) and by suffix (.tmp, .part, .crdownload, .lnk,
/// case-insensitive), sorted by path in ordinal order.
/// </summary>
public static class ChecksumFileDiscovery
{
    private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".DS_Store", "Thumbs.db", "desktop.ini",
    };

    private static readonly HashSet<string> SkipSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tmp", ".part", ".crdownload", ".lnk",
    };

    public static IReadOnlyList<string> FindAllFiles(IFileSystemEnumerator fileSystemEnumerator, string root)
    {
        ArgumentNullException.ThrowIfNull(fileSystemEnumerator);

        return fileSystemEnumerator.Enumerate(root)
            .Where(e => !e.IsDirectory)
            .Select(e => e.RelativePath)
            .Where(relativePath =>
                !SkipNames.Contains(GetFileName(relativePath)) &&
                !SkipSuffixes.Contains(GetSuffix(relativePath)))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    public static string ToAbsolutePath(string root, string relativePath) =>
        Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));

    private static string GetFileName(string relativePath)
    {
        var slashIndex = relativePath.LastIndexOf('/');
        return slashIndex < 0 ? relativePath : relativePath[(slashIndex + 1)..];
    }

    private static string GetSuffix(string relativePath)
    {
        var fileName = GetFileName(relativePath);
        var dotIndex = fileName.LastIndexOf('.');
        return dotIndex < 0 ? string.Empty : fileName[dotIndex..];
    }
}
