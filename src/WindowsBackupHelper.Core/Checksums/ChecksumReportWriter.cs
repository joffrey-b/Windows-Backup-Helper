using System.Text;

namespace WindowsBackupHelper.Core.Checksums;

/// <summary>
/// Markdown reports for checksum operations, one per ChecksumMode -- matching
/// FlacAuditReportWriter's look (summary table, "Problems at a Glance", per-folder
/// breakdown) so verification reports read the same way regardless of which kind ran.
/// Same UTF-8 no BOM, \n line endings as that writer and the checksum manifest.
/// </summary>
public static class ChecksumReportWriter
{
    public static string GenerateMarkdownForVerify(
        ChecksumVerifyResult result, string root, TimeSpan elapsed, DateTime? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var totalConsidered = result.Ok.Count + result.Changed.Count + result.Missing.Count + result.ReadErrors.Count;
        var lines = new List<string>
        {
            "# Checksum Verification Report",
            "",
            $"**Date:** {(generatedAtUtc ?? DateTime.UtcNow):yyyy-MM-dd HH:mm:ss}  ",
            $"**Library root:** `{root}`  ",
            $"**Duration:** {elapsed.TotalSeconds:F1}s  ",
            $"**Files in manifest:** {totalConsidered}",
            "",
            "## Summary",
            "",
            "| Status | Count |",
            "|--------|-------|",
            $"| ✅ OK | {result.Ok.Count} |",
            $"| 🔄 Changed | {result.Changed.Count} |",
            $"| ❓ Missing | {result.Missing.Count} |",
            $"| ⚠️ Read error | {result.ReadErrors.Count} |",
            $"| **Total**   | **{totalConsidered}** |",
            "",
        };

        var problems = new List<string>();
        foreach (var path in result.Changed.OrderBy(p => p, StringComparer.Ordinal))
        {
            problems.Add($"- 🔄 `{path}` -- changed since the manifest was written");
        }

        foreach (var path in result.Missing.OrderBy(p => p, StringComparer.Ordinal))
        {
            problems.Add($"- ❓ `{path}` -- in the manifest but missing from disk");
        }

        foreach (var (path, error) in result.ReadErrors.OrderBy(e => e.RelativePath, StringComparer.Ordinal))
        {
            problems.Add($"- ⚠️ `{path}` -- could not be read");
            problems.Add($"  - _{error}_");
        }

        AppendProblemsAndFolders(
            lines, problems, "All files matched the manifest.",
            [
                .. result.Ok.Select(p => new ReportEntry(p, "OK", "✅", "")),
                .. result.Changed.Select(p => new ReportEntry(p, "CHANGED", "🔄", "")),
                .. result.Missing.Select(p => new ReportEntry(p, "MISSING", "❓", "")),
                .. result.ReadErrors.Select(e => new ReportEntry(e.RelativePath, "READ ERROR", "⚠️", e.Error)),
            ]);

        return string.Join('\n', lines);
    }

    public static string GenerateMarkdownForGenerate(
        ChecksumGenerateResult result, string root, TimeSpan elapsed, DateTime? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var total = result.Entries.Count + result.Errors.Count;
        var lines = new List<string>
        {
            "# Checksum Generation Report",
            "",
            $"**Date:** {(generatedAtUtc ?? DateTime.UtcNow):yyyy-MM-dd HH:mm:ss}  ",
            $"**Library root:** `{root}`  ",
            $"**Duration:** {elapsed.TotalSeconds:F1}s  ",
            $"**Files scanned:** {total}",
            "",
            "## Summary",
            "",
            "| Status | Count |",
            "|--------|-------|",
            $"| ✅ Hashed | {result.Entries.Count} |",
            $"| ⚠️ Read error | {result.Errors.Count} |",
            $"| **Total**   | **{total}** |",
            "",
        };

        var problems = result.Errors
            .OrderBy(e => e.RelativePath, StringComparer.Ordinal)
            .SelectMany(e => new[] { $"- ⚠️ `{e.RelativePath}` -- could not be read", $"  - _{e.Error}_" })
            .ToList();

        AppendProblemsAndFolders(
            lines, problems, "Every discovered file was hashed successfully.",
            [
                .. result.Entries.Keys.Select(p => new ReportEntry(p, "HASHED", "✅", "")),
                .. result.Errors.Select(e => new ReportEntry(e.RelativePath, "READ ERROR", "⚠️", e.Error)),
            ]);

        return string.Join('\n', lines);
    }

    public static string GenerateMarkdownForUpdate(
        ChecksumUpdateResult result, string root, TimeSpan elapsed, DateTime? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        var lines = new List<string>
        {
            "# Checksum Update Report",
            "",
            $"**Date:** {(generatedAtUtc ?? DateTime.UtcNow):yyyy-MM-dd HH:mm:ss}  ",
            $"**Library root:** `{root}`  ",
            $"**Duration:** {elapsed.TotalSeconds:F1}s  ",
            $"**Entries in updated manifest:** {result.UpdatedEntries.Count}",
            "",
            "## Summary",
            "",
            "| Status | Count |",
            "|--------|-------|",
            $"| ➕ Added | {result.Added.Count} |",
            $"| ➖ Removed | {result.Removed.Count} |",
            $"| ⚠️ Read error | {result.Errors.Count} |",
            "",
        };

        var problems = new List<string>();
        foreach (var path in result.Added.OrderBy(p => p, StringComparer.Ordinal))
        {
            problems.Add($"- ➕ `{path}` -- newly added to the manifest");
        }

        foreach (var path in result.Removed.OrderBy(p => p, StringComparer.Ordinal))
        {
            problems.Add($"- ➖ `{path}` -- no longer on disk, removed from the manifest");
        }

        foreach (var (path, error) in result.Errors.OrderBy(e => e.RelativePath, StringComparer.Ordinal))
        {
            problems.Add($"- ⚠️ `{path}` -- could not be read");
            problems.Add($"  - _{error}_");
        }

        AppendProblemsAndFolders(
            lines, problems, "No new or removed files since the last update.",
            [
                .. result.Added.Select(p => new ReportEntry(p, "ADDED", "➕", "")),
                .. result.Removed.Select(p => new ReportEntry(p, "REMOVED", "➖", "")),
                .. result.Errors.Select(e => new ReportEntry(e.RelativePath, "READ ERROR", "⚠️", e.Error)),
            ]);

        return string.Join('\n', lines);
    }

    public static void WriteToFile(string markdown, string outputPath) =>
        File.WriteAllText(outputPath, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static void AppendProblemsAndFolders(
        List<string> lines, IReadOnlyList<string> problems, string allClearMessage, IReadOnlyList<ReportEntry> entries)
    {
        if (problems.Count > 0)
        {
            lines.Add("## ⚡ Problems at a Glance");
            lines.Add("");
            lines.AddRange(problems);
            lines.Add("");
        }
        else
        {
            lines.Add("## ✅ No Problems Found");
            lines.Add("");
            lines.Add(allClearMessage);
            lines.Add("");
        }

        lines.Add("## Results by Folder");
        lines.Add("");

        var folders = entries
            .GroupBy(e => GetFolderLabel(e.RelativePath))
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var folder in folders)
        {
            lines.Add($"### \U0001f4c1 `{folder.Key}`");
            lines.Add("");
            lines.Add("| File | Status | Details |");
            lines.Add("|------|--------|---------|");

            foreach (var entry in folder.OrderBy(e => GetFileName(e.RelativePath), StringComparer.Ordinal))
            {
                var detail = entry.Detail.Replace("|", "\\|");
                lines.Add($"| `{GetFileName(entry.RelativePath)}` | {entry.Icon} {entry.StatusLabel} | {detail} |");
            }

            lines.Add("");
        }
    }

    private static string GetFolderLabel(string relativePath)
    {
        var slashIndex = relativePath.LastIndexOf('/');
        return slashIndex < 0 ? "(root)" : relativePath[..slashIndex];
    }

    private static string GetFileName(string relativePath)
    {
        var slashIndex = relativePath.LastIndexOf('/');
        return slashIndex < 0 ? relativePath : relativePath[(slashIndex + 1)..];
    }

    private readonly record struct ReportEntry(string RelativePath, string StatusLabel, string Icon, string Detail);
}
