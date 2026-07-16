using Microsoft.Extensions.DependencyInjection;
using WindowsBackupHelper.Core.Execution;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;

namespace WindowsBackupHelper.App;

/// <summary>
/// Custom entry point (see StartupObject in the .csproj, which suppresses the WPF SDK's
/// auto-generated Main) so --run-job/--headless can be handled before any WPF startup, per
/// the handoff doc's Task Scheduler design: the scheduled task's Action is this exe with
/// "--run-job {jobId} --headless" and an explicit working directory.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var headlessArgs = HeadlessArguments.TryParse(args);
        if (headlessArgs is not null)
        {
            return RunHeadlessAsync(headlessArgs).GetAwaiter().GetResult();
        }

        // App.xaml's Application.Resources is currently empty, so the WPF markup compiler
        // doesn't generate an InitializeComponent() to call here — if real app-level
        // resources are added to App.xaml later, verify one appears (App.g.cs) and call it.
        var app = new App();
        return app.Run();
    }

    private static async Task<int> RunHeadlessAsync(HeadlessArguments headlessArgs)
    {
        try
        {
            var services = await CompositionRoot.BuildAsync(includeUi: false);

            var jobRepository = services.GetRequiredService<JobRepository>();
            var job = await jobRepository.GetByIdAsync(headlessArgs.JobId);
            if (job is null)
            {
                await Console.Error.WriteLineAsync($"Job '{headlessArgs.JobId}' was not found.");
                return 2;
            }

            if (job.IsDeleted)
            {
                await Console.Error.WriteLineAsync($"Job '{job.Name}' has been deleted; skipping.");
                return 0;
            }

            if (!job.IsEnabled)
            {
                await Console.Error.WriteLineAsync($"Job '{job.Name}' is disabled; skipping.");
                return 0;
            }

            var folderPairRepository = services.GetRequiredService<FolderPairRepository>();
            var pairs = await folderPairRepository.GetByJobIdAsync(job.Id);

            var jobExecutionService = services.GetRequiredService<JobExecutionService>();
            var runHistory = await jobExecutionService.RunJobAsync(job, pairs, RunTriggerType.Scheduled, dryRun: false);

            Console.WriteLine($"Job '{job.Name}' finished: {runHistory.OverallOutcome}.");
            return runHistory.OverallOutcome is RunOutcome.Success or RunOutcome.SuccessWithMismatches ? 0 : 1;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Headless run failed: {ex}");
            return 1;
        }
    }
}

/// <summary>
/// Hand-rolled --run-job/--headless parsing rather than pulling in System.CommandLine
/// (still pre-1.0 as of this writing).
/// </summary>
internal sealed record HeadlessArguments(string JobId)
{
    public static HeadlessArguments? TryParse(string[] args)
    {
        string? jobId = null;
        var headless = false;

        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--run-job", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                jobId = args[++i];
            }
            else if (string.Equals(args[i], "--headless", StringComparison.OrdinalIgnoreCase))
            {
                headless = true;
            }
        }

        return headless && jobId is not null ? new HeadlessArguments(jobId) : null;
    }
}
