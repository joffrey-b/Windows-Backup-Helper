using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WindowsBackupHelper.App.ViewModels;

namespace WindowsBackupHelper.App;

/// <summary>
/// Interactive entry point. Program.cs's Main calls this after determining the process was
/// NOT launched for a headless scheduled run; see CompositionRoot for the shared DI wiring.
/// </summary>
public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            Services = await CompositionRoot.BuildAsync(includeUi: true);

            var mainViewModel = Services.GetRequiredService<MainViewModel>();
            await mainViewModel.InitializeAsync();

            Services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Windows Backup Helper failed to start:\n\n{ex}",
                "Startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
