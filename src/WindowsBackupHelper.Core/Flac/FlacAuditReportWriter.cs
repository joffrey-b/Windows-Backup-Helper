using System.Text;

namespace WindowsBackupHelper.Core.Flac;

/// <summary>
/// Ports flac_audit_windows_linux.py's generate_markdown: a summary table, a
/// "Problems at a Glance" section (non-OK files only), and a per-folder breakdown
/// (--errors-only omits OK rows from tables but still counts them in the summary). Same
/// status icons, forward-slash relative paths, UTF-8 no BOM, \n line endings as the
/// checksum manifest.
/// </summary>
public static class FlacAuditReportWriter
{
    public static string GenerateMarkdown(
        IReadOnlyList<FlacFileResult> results, string root, TimeSpan elapsed, bool errorsOnly, DateTime? generatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        var lines = new List<string>
        {
            "# FLAC Library Audit Report",
            "",
            $"**Date:** {(generatedAtUtc ?? DateTime.UtcNow):yyyy-MM-dd HH:mm:ss}  ",
            $"**Library root:** `{root}`  ",
            $"**Duration:** {elapsed.TotalSeconds:F1}s  ",
            $"**Files scanned:** {results.Count}",
            "",
        };

        var okCount = results.Count(r => r.Status == FlacFileStatus.Ok);
        var warnCount = results.Count(r => r.Status == FlacFileStatus.Warning);
        var errorCount = results.Count(r => r.Status == FlacFileStatus.Error);

        lines.Add("## Summary");
        lines.Add("");
        lines.Add("| Status | Count |");
        lines.Add("|--------|-------|");
        lines.Add($"| {Icon(FlacFileStatus.Ok)} OK       | {okCount} |");
        lines.Add($"| {Icon(FlacFileStatus.Warning)} Warning  | {warnCount} |");
        lines.Add($"| {Icon(FlacFileStatus.Error)} Error    | {errorCount} |");
        lines.Add($"| **Total**   | **{results.Count}** |");
        lines.Add("");

        var problems = results.Where(r => r.Status != FlacFileStatus.Ok).ToList();
        if (problems.Count > 0)
        {
            lines.Add("## ⚡ Problems at a Glance");
            lines.Add("");
            foreach (var result in problems)
            {
                lines.Add($"- {Icon(result.Status)} `{result.RelativePath}`");
                foreach (var message in result.Messages)
                {
                    lines.Add($"  - _{message}_");
                }
            }

            lines.Add("");
        }
        else
        {
            lines.Add("## ✅ No Problems Found");
            lines.Add("");
            lines.Add("All files passed verification.");
            lines.Add("");
        }

        lines.Add("## Results by Folder");
        lines.Add("");

        var reportResults = errorsOnly ? results.Where(r => r.Status != FlacFileStatus.Ok) : results;
        var folders = reportResults
            .GroupBy(GetFolderLabel)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var folder in folders)
        {
            var folderResults = folder.ToList();
            var folderWarnings = folderResults.Count(r => r.Status == FlacFileStatus.Warning);
            var folderErrors = folderResults.Count(r => r.Status == FlacFileStatus.Error);

            var badge = folderErrors > 0
                ? $"{Icon(FlacFileStatus.Error)} {folderErrors} error(s)"
                : folderWarnings > 0
                    ? $"{Icon(FlacFileStatus.Warning)} {folderWarnings} warning(s)"
                    : $"{Icon(FlacFileStatus.Ok)} all OK";

            lines.Add($"### \U0001f4c1 `{folder.Key}` -- {badge}");
            lines.Add("");
            lines.Add("| File | Status | Details |");
            lines.Add("|------|--------|---------|");

            foreach (var result in folderResults.OrderBy(GetFileName, StringComparer.Ordinal))
            {
                var detail = string.Join("; ", result.Messages).Replace("|", "\\|");
                lines.Add($"| `{GetFileName(result)}` | {Icon(result.Status)} {result.Status.ToString().ToUpperInvariant()} | {detail} |");
            }

            lines.Add("");
        }

        return string.Join('\n', lines);
    }

    public static void WriteToFile(string markdown, string outputPath) =>
        File.WriteAllText(outputPath, markdown, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    private static string Icon(FlacFileStatus status) => status switch
    {
        FlacFileStatus.Ok => "✅",
        FlacFileStatus.Warning => "⚠️",
        FlacFileStatus.Error => "❌",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private static string GetFolderLabel(FlacFileResult result)
    {
        var slashIndex = result.RelativePath.LastIndexOf('/');
        return slashIndex < 0 ? "(root)" : result.RelativePath[..slashIndex];
    }

    private static string GetFileName(FlacFileResult result) => GetFileName(result.RelativePath);

    private static string GetFileName(string relativePath)
    {
        var slashIndex = relativePath.LastIndexOf('/');
        return slashIndex < 0 ? relativePath : relativePath[(slashIndex + 1)..];
    }
}
