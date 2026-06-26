using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PDownloader.Models;
using PDownloader.Services;
using PDownloader.Utils;

namespace PDownloader.ViewModels.Pages;

public partial class DownloadsViewModel : ObservableObject
{
    public ObservableCollection<DownloadItemDto> Downloads { get; } = new();

    [ObservableProperty] private bool   _isLoading = false;
    [ObservableProperty] private bool   _isEmpty   = true;
    [ObservableProperty] private string _statusText = "Sẵn sàng";

    public DownloadsViewModel()
    {
        // Subscribe to live updates from Core
        DownloadsChannel.OnProgress += OnProgress;
        DownloadsChannel.OnList     += OnList;

        RequestRefresh();

    }

    public void RequestRefresh()
    {
        IsLoading = true;
        // Ask Core for current list
        ConfluxManager.cfsPDownloaderCore?.Send("downloader-svc-getlist", string.Empty);
    }

    // ── Receive full list (response to downloader-svc-getlist) ───────────────
    private void OnList(List<DownloadItemDto> items)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            Downloads.Clear();
            foreach (var item in items)
                Downloads.Add(item);
            IsEmpty   = Downloads.Count == 0;
            IsLoading = false;
            StatusText = $"{Downloads.Count} tác vụ";
        });
    }

    // ── Receive one item update (muxt-download-progress) ─────────────────────
    private void OnProgress(DownloadItemDto dto)
    {
        App.Current.Dispatcher.Invoke(() =>
        {
            var existing = Downloads.FirstOrDefault(d => d.Id == dto.Id);
            if (existing != null)
            {
                // Update in-place
                int idx = Downloads.IndexOf(existing);
                Downloads[idx] = dto;
            }
            else
            {
                Downloads.Insert(0, dto);
            }
            IsEmpty = Downloads.Count == 0;
        });
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void Pause(DownloadItemDto? item)
    {
        if (item == null) return;
        ConfluxManager.cfsPDownloaderCore?.Send("runner-pause", item.Id);
    }

    [RelayCommand]
    private void Resume(DownloadItemDto? item)
    {
        if (item == null) return;
        ConfluxManager.cfsPDownloaderCore?.Send("runner-resume", item.Id);
    }

    [RelayCommand]
    private void Cancel(DownloadItemDto? item)
    {
        if (item == null) return;
        ConfluxManager.cfsPDownloaderCore?.Send("runner-cancel", item.Id);
    }

    [RelayCommand]
    private void Retry(DownloadItemDto? item)
    {
        if (item == null) return;
        ConfluxManager.cfsPDownloaderCore?.Send("runner-retry", item.Id);
    }

    [RelayCommand]
    private void OpenFile(DownloadItemDto? item)
    {
        if (item == null || !System.IO.File.Exists(item.SavePath)) return;
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo(item.SavePath) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder(DownloadItemDto? item)
    {
        if (item == null) return;
        string? folder = System.IO.Path.GetDirectoryName(item.SavePath);
        if (folder == null || !System.IO.Directory.Exists(folder)) return;
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo("explorer.exe", folder) { UseShellExecute = true });
    }

    [RelayCommand]
    private void Refresh() => RequestRefresh();
}
