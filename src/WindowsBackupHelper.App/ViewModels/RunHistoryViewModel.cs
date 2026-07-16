using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WindowsBackupHelper.Core.Models;
using WindowsBackupHelper.Core.Repositories;

namespace WindowsBackupHelper.App.ViewModels;

/// <summary>A RunHistory row paired with its job's display name and whether that job is still
/// active (not soft-deleted), for the Run History grid.</summary>
public sealed record RunHistoryRow(RunHistory Run, string JobName, bool JobReferenced);

/// <summary>A FolderPairRunResult row paired with whether its folder pair is still active
/// (not soft-deleted), for the per-run results grid.</summary>
public sealed record FolderPairRunResultRow(FolderPairRunResult Result, bool PairReferenced);

public sealed partial class RunHistoryViewModel(
    RunHistoryRepository runHistoryRepository,
    FolderPairRunResultRepository runResultRepository,
    JobRepository jobRepository,
    FolderPairRepository folderPairRepository) : ObservableObject
{
    public ObservableCollection<RunHistoryRow> Runs { get; } = [];

    public ObservableCollection<FolderPairRunResultRow> SelectedRunResults { get; } = [];

    [ObservableProperty]
    private RunHistoryRow? _selectedRun;

    partial void OnSelectedRunChanged(RunHistoryRow? value) => _ = LoadResultsAsync(value);

    public async Task LoadAsync()
    {
        var rows = new List<RunHistoryRow>();
        // Includes soft-deleted jobs too — a deleted job's history should still show up, with
        // JobReferenced = false, rather than vanishing from the list entirely.
        foreach (var job in await jobRepository.GetAllIncludingDeletedAsync())
        {
            foreach (var run in await runHistoryRepository.GetByJobIdAsync(job.Id))
            {
                rows.Add(new RunHistoryRow(run, job.Name, JobReferenced: !job.IsDeleted));
            }
        }

        Runs.Clear();
        foreach (var row in rows.OrderByDescending(r => r.Run.StartedUtc))
        {
            Runs.Add(row);
        }
    }

    private async Task LoadResultsAsync(RunHistoryRow? row)
    {
        SelectedRunResults.Clear();
        if (row is null)
        {
            return;
        }

        foreach (var result in await runResultRepository.GetByRunHistoryIdAsync(row.Run.Id))
        {
            var pair = await folderPairRepository.GetByIdAsync(result.FolderPairId);
            SelectedRunResults.Add(new FolderPairRunResultRow(result, PairReferenced: pair is { IsDeleted: false }));
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedRunAsync()
    {
        if (SelectedRun is null)
        {
            return;
        }

        if (MessageBox.Show(
                $"Delete this run of '{SelectedRun.JobName}' from {SelectedRun.Run.StartedUtc}? This cannot be undone.",
                "Delete run", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await runHistoryRepository.DeleteAsync(SelectedRun.Run.Id);
        Runs.Remove(SelectedRun);
        SelectedRun = null;
    }

    [RelayCommand]
    private async Task ClearAllHistoryAsync()
    {
        if (Runs.Count == 0)
        {
            return;
        }

        if (MessageBox.Show(
                "Delete ALL run history for every job? This cannot be undone.",
                "Clear all history", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        await runHistoryRepository.DeleteAllAsync();
        SelectedRun = null;
        Runs.Clear();
    }
}
