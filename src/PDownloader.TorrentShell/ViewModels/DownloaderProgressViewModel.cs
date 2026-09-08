// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program. If not, see <https://www.gnu.org/licenses/>.
//
// Copyright (C) Song Mai Software.

namespace PDownloader.TorrentShell.ViewModels;

public partial class DownloaderProgressViewModel : ObservableObject
{
    private readonly TorrentShellService _torrentShellService;

    [ObservableProperty]
    private TorrentShellConfig _torrentConfig;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private string _overallDownloadedText = "0 B";

    [ObservableProperty]
    private string _overallTotalText = "0 B";

    [ObservableProperty]
    private string _overallSpeedText = "–";

    [ObservableProperty]
    private string _progressSummary = string.Empty;

    [ObservableProperty]
    private bool _isComplete;

    public ObservableCollection<TorrentFileViewModel> Files { get; } = [];

    public DownloaderProgressViewModel(
        TorrentShellConfig torrentConfig,
        TorrentShellService torrentShellService)
    {
        _torrentConfig = torrentConfig;
        _torrentShellService = torrentShellService;
        _torrentShellService.ProgressReceived += TorrentShellService_ProgressReceived;

        foreach (TorrentFileViewModel file in TorrentConfig.Files.Where(file => file.IsSelected))
        {
            file.PropertyChanged += File_PropertyChanged;
            Files.Add(file);
            if (_torrentShellService.TryGetLatestProgress(file.DownloadId, out DownloadItemDto? progress)
                && progress is not null)
            {
                file.ApplyProgress(progress);
            }
        }

        TranslationSource.Instance.PropertyChanged += TranslationSource_PropertyChanged;
        RefreshSummary();
    }

    [RelayCommand]
    private void Pause(TorrentFileViewModel? file)
    {
        if (file?.CanPause == true)
        {
            _torrentShellService.Pause(file.DownloadId);
        }
    }

    [RelayCommand]
    private void Resume(TorrentFileViewModel? file)
    {
        if (file?.CanResume == true)
        {
            _torrentShellService.Resume(file.DownloadId);
        }
    }

    [RelayCommand]
    private void Retry(TorrentFileViewModel? file)
    {
        if (file?.HasError == true)
        {
            _torrentShellService.Retry(file.DownloadId);
        }
    }

    [RelayCommand]
    private void Cancel(TorrentFileViewModel? file)
    {
        if (file?.CanCancel == true)
        {
            _torrentShellService.Cancel(file.DownloadId);
        }
    }

    [RelayCommand]
    private void OpenFile(TorrentFileViewModel? file)
    {
        if (file is null || !File.Exists(file.SavePath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(file.SavePath) { UseShellExecute = true });
    }

    [RelayCommand]
    private void OpenFolder(TorrentFileViewModel? file)
    {
        if (file is null || string.IsNullOrWhiteSpace(file.SavePath))
        {
            return;
        }

        string? folder = Path.GetDirectoryName(file.SavePath);
        if (folder is null || !Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(
            "explorer.exe",
            $"/select,\"{file.SavePath}\"")
        {
            UseShellExecute = true
        });
    }

    [RelayCommand]
    private void Close() => Application.Current.Shutdown();

    private void TorrentShellService_ProgressReceived(DownloadItemDto progress)
    {
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            TorrentFileViewModel? file = Files.FirstOrDefault(candidate =>
                string.Equals(candidate.DownloadId, progress.Id, StringComparison.Ordinal));
            file?.ApplyProgress(progress);
        }));
    }

    private void File_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TorrentFileViewModel.Progress)
            or nameof(TorrentFileViewModel.Status)
            or nameof(TorrentFileViewModel.SpeedBps)
            or nameof(TorrentFileViewModel.DownloadedText)
            or nameof(TorrentFileViewModel.TotalText))
        {
            RefreshSummary();
        }
    }

    private void TranslationSource_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        long totalBytes = Files.Sum(file => file.Length);
        double weightedProgress = totalBytes > 0
            ? Files.Sum(file => file.Length * file.Progress) / totalBytes
            : Files.Count > 0
                ? Files.Average(file => file.Progress)
                : 0;
        int completed = Files.Count(file => file.IsCompleted);
        double speed = Files.Sum(file => file.SpeedBps);

        OverallProgress = Math.Clamp(weightedProgress, 0, 100);
        OverallDownloadedText = TorrentFileViewModel.FormatBytes(
            (long)Math.Round(totalBytes * OverallProgress / 100.0));
        OverallTotalText = TorrentFileViewModel.FormatBytes(totalBytes);
        OverallSpeedText = speed > 0
            ? $"{TorrentFileViewModel.FormatBytes((long)speed)}/s"
            : "–";
        ProgressSummary = LanguageBase.GetLangValue(
            "torrent_shell_progress_summary",
            completed,
            Files.Count);
        IsComplete = Files.Count > 0 && completed == Files.Count;
    }

}
