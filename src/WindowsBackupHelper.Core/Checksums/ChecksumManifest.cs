using System.Text;

namespace WindowsBackupHelper.Core.Checksums;

/// <summary>
/// Reads/writes the sha256sum-compatible manifest format produced by
/// checksums_windows_linux.py: "&lt;64-hex-digest&gt;&lt;TWO SPACES&gt;&lt;relative-path&gt;\n", paths
/// always forward-slash normalized, entries sorted by path in ordinal (codepoint) order,
/// UTF-8 with no BOM, Unix line endings — so a manifest from one tool verifies cleanly with
/// the other, including via plain `sha256sum -c` on Linux.
///
/// Two C# defaults would silently break this if not overridden: StreamWriter emits
/// Environment.NewLine (\r\n on Windows) by default, and Encoding.UTF8 emits a BOM by
/// default — both are overridden explicitly below.
/// </summary>
public static class ChecksumManifest
{
    private static readonly UTF8Encoding NoBomUtf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static void Write(IReadOnlyDictionary<string, string> entries, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(entries);

        using var writer = new StreamWriter(outputPath, append: false, NoBomUtf8) { NewLine = "\n" };
        foreach (var relativePath in entries.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            writer.WriteLine($"{entries[relativePath]}  {relativePath}");
        }
    }

    public static Dictionary<string, string> Read(string manifestPath)
    {
        var entries = new Dictionary<string, string>();

        foreach (var rawLine in File.ReadLines(manifestPath, Encoding.UTF8))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separatorIndex = line.IndexOf("  ", StringComparison.Ordinal);
            if (separatorIndex < 0)
            {
                continue;
            }

            var digest = line[..separatorIndex].Trim();
            // Normalize to forward slashes in case the manifest was hand-edited on Windows.
            var relativePath = line[(separatorIndex + 2)..].Replace('\\', '/');
            entries[relativePath] = digest;
        }

        return entries;
    }
}
