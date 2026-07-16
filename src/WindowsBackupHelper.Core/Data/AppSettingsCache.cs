using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;

namespace WindowsBackupHelper.Core.Data;

/// <summary>
/// Holds the latest-loaded AppSettings in memory so synchronous callers (like
/// IFlacProcessRunner's path resolver, which can't await a repository call) can read it
/// without a round trip. Call LoadAsync once at startup and Update whenever Settings are saved.
/// </summary>
public sealed class AppSettingsCache(AppSettingsRepository repository)
{
    private AppSettings? _current;

    public AppSettings Current => _current ?? throw new InvalidOperationException("AppSettings has not been loaded yet — call LoadAsync at startup.");

    public async Task LoadAsync()
    {
        _current = await repository.GetAsync().ConfigureAwait(false)
            ?? throw new InvalidOperationException("AppSettings row is missing — DatabaseSeeder should have created it.");
    }

    public void Update(AppSettings settings) => _current = settings;
}
