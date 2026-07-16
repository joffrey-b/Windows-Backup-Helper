using System.Text;
using System.Text.RegularExpressions;
using WindowsBackupHelper.Core.Models;

namespace WindowsBackupHelper.Core.Exclusions;

public sealed record ExclusionResolutionResult
{
    public IReadOnlyList<string> ExcludeFileTokens { get; init; } = [];
    public IReadOnlyList<string> ExcludeDirectoryTokens { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Evaluates the union of applicable ExclusionRule rows against a folder pair's file tree,
/// producing the literal tokens to hand to Robocopy's /XF and /XD.
///
/// Robocopy's /XF and /XD natively support bare-name wildcards (e.g. "*.tmp", "Thumbs.db")
/// applied at every directory level it recurses into — Robocopy does that matching itself,
/// so those rules pass straight through as a single token and never touch the filesystem
/// enumerator ("pattern collapsing"). Everything else (regex rules, and wildcard rules that
/// include a path separator, e.g. "Disc 1/*.flac") needs this process to pre-enumerate the
/// tree and resolve the rule to the concrete relative paths it matches, since Robocopy has
/// no regex support and no notion of a path-scoped wildcard.
/// </summary>
public sealed class ExclusionRuleResolver(IFileSystemEnumerator fileSystemEnumerator)
{
    /// <summary>Guards against catastrophic backtracking in a user-authored regex pattern.</summary>
    public static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Conservative headroom under CreateProcess's ~32,767-char command-line ceiling, past
    /// which a resolved-argument warning should surface at job-save/edit time.
    /// </summary>
    public const int CommandLineLengthWarningThreshold = 30_000;

    public ExclusionResolutionResult Resolve(IReadOnlyList<ExclusionRule> rules, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(rules);

        var excludeFiles = new List<string>();
        var excludeDirs = new List<string>();
        var warnings = new List<string>();

        var enabledRules = rules.Where(r => r.IsEnabled).ToList();
        var collapsible = enabledRules.Where(IsCollapsible).ToList();
        var needsEnumeration = enabledRules.Except(collapsible).ToList();

        foreach (var rule in collapsible)
        {
            AddToken(rule.TargetType, rule.Pattern, excludeFiles, excludeDirs);
        }

        if (needsEnumeration.Count > 0)
        {
            var compiled = needsEnumeration.Select(r => (Rule: r, Regex: BuildRegex(r))).ToList();

            foreach (var entry in fileSystemEnumerator.Enumerate(rootPath))
            {
                foreach (var (rule, regex) in compiled)
                {
                    if (!MatchesTargetType(rule.TargetType, entry.IsDirectory))
                    {
                        continue;
                    }

                    bool isMatch;
                    try
                    {
                        isMatch = regex.IsMatch(entry.RelativePath);
                    }
                    catch (RegexMatchTimeoutException)
                    {
                        warnings.Add($"Pattern '{rule.Pattern}' timed out matching '{entry.RelativePath}' and was skipped for that path.");
                        continue;
                    }

                    if (isMatch)
                    {
                        var resolvedTargetType = rule.TargetType == ExclusionTargetType.Both
                            ? (entry.IsDirectory ? ExclusionTargetType.Directory : ExclusionTargetType.File)
                            : rule.TargetType;
                        AddToken(resolvedTargetType, entry.RelativePath, excludeFiles, excludeDirs);
                    }
                }
            }
        }

        var estimatedLength = EstimateCommandLineLength(excludeFiles, excludeDirs);
        if (estimatedLength > CommandLineLengthWarningThreshold)
        {
            warnings.Add(
                $"Estimated resolved exclusion argument length (~{estimatedLength:N0} chars) is approaching " +
                "CreateProcess's ~32,767-char command-line limit. Narrow the rule scope before running this job.");
        }

        return new ExclusionResolutionResult
        {
            ExcludeFileTokens = excludeFiles,
            ExcludeDirectoryTokens = excludeDirs,
            Warnings = warnings,
        };
    }

    /// <summary>
    /// A rule collapses to a single native Robocopy token when it's a wildcard pattern with no
    /// path separator — i.e. exactly the shape of the Python scripts' own skip lists
    /// (*.tmp, Thumbs.db, desktop.ini). Regex rules and path-scoped wildcards always need
    /// enumeration since Robocopy can't evaluate either natively.
    /// </summary>
    internal static bool IsCollapsible(ExclusionRule rule) =>
        rule.PatternType == ExclusionPatternType.Wildcard && !ContainsPathSeparator(rule.Pattern);

    private static bool ContainsPathSeparator(string pattern) => pattern.Contains('/') || pattern.Contains('\\');

    private static bool MatchesTargetType(ExclusionTargetType targetType, bool isDirectory) => targetType switch
    {
        ExclusionTargetType.File => !isDirectory,
        ExclusionTargetType.Directory => isDirectory,
        ExclusionTargetType.Both => true,
        _ => false,
    };

    private static void AddToken(ExclusionTargetType targetType, string token, List<string> excludeFiles, List<string> excludeDirs)
    {
        if (targetType is ExclusionTargetType.File or ExclusionTargetType.Both)
        {
            excludeFiles.Add(token);
        }

        if (targetType is ExclusionTargetType.Directory or ExclusionTargetType.Both)
        {
            excludeDirs.Add(token);
        }
    }

    private static Regex BuildRegex(ExclusionRule rule)
    {
        var pattern = rule.PatternType == ExclusionPatternType.Wildcard
            ? WildcardToAnchoredRegexPattern(rule.Pattern)
            : rule.Pattern;

        // Case-insensitive by default: Windows filesystems are case-insensitive.
        return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexMatchTimeout);
    }

    /// <summary>
    /// Translates a path-scoped wildcard (e.g. "Disc 1/*.flac") into a regex anchored to match
    /// the *entire* relative path — deliberately not a "match anywhere" pattern, since Robocopy
    /// itself already handles the anywhere-at-any-depth case for the collapsible, bare-name rules.
    /// </summary>
    internal static string WildcardToAnchoredRegexPattern(string wildcard)
    {
        var builder = new StringBuilder("^");
        foreach (var ch in wildcard)
        {
            switch (ch)
            {
                case '*':
                    builder.Append(".*");
                    break;
                case '?':
                    builder.Append('.');
                    break;
                default:
                    builder.Append(Regex.Escape(ch.ToString()));
                    break;
            }
        }

        builder.Append('$');
        return builder.ToString();
    }

    private static int EstimateCommandLineLength(IReadOnlyList<string> excludeFiles, IReadOnlyList<string> excludeDirs) =>
        excludeFiles.Sum(f => f.Length + 3) + excludeDirs.Sum(d => d.Length + 3);
}
