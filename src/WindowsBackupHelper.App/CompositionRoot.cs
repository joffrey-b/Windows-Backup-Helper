using System.Data;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using WindowsBackupHelper.App.Services;
using WindowsBackupHelper.App.ViewModels;
using WindowsBackupHelper.Core.Checksums;
using WindowsBackupHelper.Core.Credentials;
using WindowsBackupHelper.Core.Data;
using WindowsBackupHelper.Core.Elevation;
using WindowsBackupHelper.Core.Execution;
using WindowsBackupHelper.Core.Exclusions;
using WindowsBackupHelper.Core.Flac;
using WindowsBackupHelper.Core.Notifications;
using WindowsBackupHelper.Core.Repositories;
using WindowsBackupHelper.Core.Robocopy;
using WindowsBackupHelper.Core.Scheduling;
using WindowsBackupHelper.Core.Smb;
using WindowsBackupHelper.Win.Credentials;
using WindowsBackupHelper.Win.Elevation;
using WindowsBackupHelper.Win.Flac;
using WindowsBackupHelper.Win.Robocopy;
using WindowsBackupHelper.Win.Scheduling;
using WindowsBackupHelper.Win.Smb;

namespace WindowsBackupHelper.App;

/// <summary>
/// Single source of truth for DI registration, shared by the interactive WPF startup
/// (App.xaml.cs) and the headless scheduled-task entry point (Program.cs) — both need the
/// exact same repositories/services wired up identically, just with or without the UI layer.
/// </summary>
public static class CompositionRoot
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsBackupHelper");

    public static async Task<IServiceProvider> BuildAsync(bool includeUi)
    {
        Directory.CreateDirectory(AppDataDirectory);
        var databasePath = Path.Combine(AppDataDirectory, "app.db");
        var logDirectory = Path.Combine(AppDataDirectory, "logs");

        var services = new ServiceCollection();

        var connection = new SqliteConnectionFactory(databasePath).OpenConnection();
        new SchemaMigrator().Migrate(connection);
        services.AddSingleton<IDbConnection>(connection);

        services.AddSingleton<RobocopyOptionSetRepository>();
        services.AddSingleton<ExclusionRuleRepository>();
        services.AddSingleton<CredentialTargetRepository>();
        services.AddSingleton<VerificationSettingsRepository>();
        services.AddSingleton<AppSettingsRepository>();
        services.AddSingleton<RunHistoryRepository>();
        services.AddSingleton<FolderPairRunResultRepository>();
        services.AddSingleton<JobRepository>();
        services.AddSingleton<FolderPairRepository>();
        services.AddSingleton<ScheduleMetadataRepository>();

        services.AddSingleton<AppSettingsCache>();
        services.AddSingleton<DatabaseSeeder>();

        services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        services.AddSingleton<ISmbConnectionManager, WNetSmbConnectionManager>();
        services.AddSingleton<IRobocopyProcessRunner>(_ => new RobocopyProcessRunner());
        services.AddSingleton<IFileSystemEnumerator, DirectoryFileSystemEnumerator>();
        services.AddSingleton<IFileHasher, Sha256FileHasher>();
        services.AddSingleton<IElevationService, ElevationService>();
        services.AddSingleton<ITaskSchedulerService, WindowsTaskSchedulerService>();
        services.AddSingleton<IFlacProcessRunner>(sp =>
        {
            var settingsCache = sp.GetRequiredService<AppSettingsCache>();
            return new FlacProcessRunner(() =>
                settingsCache.Current.FlacExecutablePath is { Length: > 0 } path ? path : "flac.exe");
        });

        // Headless runs have no tray to show a balloon tip from, so they get the no-op
        // notification service; the interactive app swaps in the real NotifyIcon-backed one.
        if (includeUi)
        {
            services.AddSingleton<INotificationService, NotifyIconNotificationService>();
        }
        else
        {
            services.AddSingleton<INotificationService, NoOpNotificationService>();
        }

        services.AddSingleton<VerificationRunner>();

        services.AddSingleton(sp => new JobExecutionService(
            sp.GetRequiredService<RobocopyOptionSetRepository>(),
            sp.GetRequiredService<ExclusionRuleRepository>(),
            sp.GetRequiredService<CredentialTargetRepository>(),
            sp.GetRequiredService<VerificationSettingsRepository>(),
            sp.GetRequiredService<AppSettingsRepository>(),
            sp.GetRequiredService<RunHistoryRepository>(),
            sp.GetRequiredService<FolderPairRunResultRepository>(),
            sp.GetRequiredService<ICredentialStore>(),
            sp.GetRequiredService<ISmbConnectionManager>(),
            sp.GetRequiredService<IRobocopyProcessRunner>(),
            sp.GetRequiredService<IFileSystemEnumerator>(),
            sp.GetRequiredService<VerificationRunner>(),
            sp.GetRequiredService<INotificationService>(),
            logDirectory));

        if (includeUi)
        {
            services.AddSingleton<JobsViewModel>();
            services.AddSingleton<CredentialTargetsViewModel>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<RunHistoryViewModel>();
            services.AddSingleton<StandaloneVerificationViewModel>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<MainWindow>();
        }

        var serviceProvider = services.BuildServiceProvider();

        await serviceProvider.GetRequiredService<DatabaseSeeder>().EnsureSeedDataAsync();
        await serviceProvider.GetRequiredService<AppSettingsCache>().LoadAsync();

        return serviceProvider;
    }
}
