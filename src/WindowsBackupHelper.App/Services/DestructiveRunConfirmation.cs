using System.Windows;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Robocopy;

namespace WindowsBackupHelper.App.Services;

/// <summary>
/// The explicit confirmation gate the handoff doc calls for: an accidentally-reversed
/// FolderPair combined with /MIR could wipe the NAS, which is the user's actual source of
/// truth, so any run whose resolved options include /MIR, /PURGE, or /MOVE must be confirmed
/// before it's allowed to proceed. Headless/scheduled runs skip this entirely — there's
/// nobody to confirm with — which is why this lives in the App layer, not JobExecutionService.
/// </summary>
public static class DestructiveRunConfirmation
{
    public static bool IsDestructive(ResolvedRobocopyOptions options) => options.Mirror || options.Purge || options.Move;

    /// <returns>true if the run should proceed (nothing destructive, or the user confirmed); false if they cancelled.</returns>
    public static bool ConfirmIfNeeded(IReadOnlyList<(FolderPair Pair, ResolvedRobocopyOptions Options)> pairs)
    {
        var destructivePairs = pairs.Where(p => IsDestructive(p.Options)).ToList();
        if (destructivePairs.Count == 0)
        {
            return true;
        }

        var lines = destructivePairs.Select(p =>
        {
            var flags = string.Join(", ", new[]
            {
                p.Options.Mirror ? "/MIR" : null,
                p.Options.Purge ? "/PURGE" : null,
                p.Options.Move ? "/MOVE" : null,
            }.Where(f => f is not null));
            return $"  {p.Pair.SourcePath}\n    -> {p.Pair.DestinationPath}   [{flags}]";
        });

        var message =
            "This run includes destructive Robocopy options that can delete or move files at " +
            "the destination if the source no longer has them:\n\n" +
            string.Join("\n\n", lines) +
            "\n\nDouble-check the source and destination are the right way around before continuing.";

        var result = MessageBox.Show(message, "Confirm destructive run", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        return result == MessageBoxResult.Yes;
    }
}
