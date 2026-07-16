using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;

namespace WindowsBackupHelper.Core.Data;

/// <summary>
/// Ensures the AppSettings singleton row exists after a fresh migration. AppSettings.
/// DefaultRobocopyOptionSetId is a NOT NULL FK, so a brand-new database needs a default
/// RobocopyOptionSet seeded before AppSettings can be inserted — this is the one place
/// Retries/WaitSeconds are guaranteed non-null for the whole options cascade.
/// </summary>
public sealed class DatabaseSeeder(AppSettingsRepository appSettingsRepository, RobocopyOptionSetRepository optionSetRepository)
{
    public async Task EnsureSeedDataAsync()
    {
        if (await appSettingsRepository.GetAsync().ConfigureAwait(false) is not null)
        {
            return;
        }

        var defaultOptionSet = new RobocopyOptionSet
        {
            Id = Guid.NewGuid().ToString(),
            CopySubdirectories = true,
            CopyEmptySubdirectories = true,
            Retries = 3,
            WaitSeconds = 5,
        };
        await optionSetRepository.InsertAsync(defaultOptionSet).ConfigureAwait(false);

        await appSettingsRepository.InsertAsync(new AppSettings
        {
            DefaultRobocopyOptionSetId = defaultOptionSet.Id,
        }).ConfigureAwait(false);
    }
}
