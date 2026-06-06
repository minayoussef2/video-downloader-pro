using System.Collections.Concurrent;
using VideoDownloaderPro.Models;
using VideoDownloaderPro.Services;

namespace VideoDownloaderPro.Core;

/// <summary>
/// Manages the download queue with configurable concurrency.
/// Fixes V2 bug: always downloading all at once regardless of settings.
/// </summary>
public class QueueManager
{
    private readonly ConcurrentDictionary<string, (DownloadItem Item, YtDlpWrapper Wrapper)> _activeDownloads = new();
    private readonly ConcurrentDictionary<string, bool> _reservedPaths = new(System.StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastProgressUpdate = new();
    private readonly SemaphoreSlim _concurrencySemaphore;
    private readonly CancellationTokenSource _cts = new();
    private int _maxConcurrent;

    public event Action<DownloadItem>? OnDownloadStarted;
    public event Action<DownloadItem, string>? OnDownloadCompleted;
    public event Action<DownloadItem, double, string>? OnProgressUpdated;

    public int ActiveCount => _activeDownloads.Count;

    public QueueManager(int maxConcurrentDownloads = 1)
    {
        _maxConcurrent = Math.Max(1, maxConcurrentDownloads);
        _concurrencySemaphore = new SemaphoreSlim(_maxConcurrent, Math.Max(_maxConcurrent, 64));
    }

    /// <summary>
    /// Update the maximum concurrent downloads (respects currently running downloads).
    /// </summary>
    public void SetMaxConcurrent(int max)
    {
        _maxConcurrent = Math.Max(1, max);
    }

    /// <summary>
    /// Start downloading a single item, respecting concurrency limits.
    /// </summary>
    public async Task StartDownloadAsync(DownloadItem item, string? referer = null)
    {
        // Wait for a slot
        await _concurrencySemaphore.WaitAsync(_cts.Token);

        string? reservedPath = null;
        try
        {
            if (item.Status == DownloadStatus.Cancelled) return;

            var wrapper = new YtDlpWrapper();
            _activeDownloads[item.Id] = (item, wrapper);

            item.Status = DownloadStatus.Downloading;
            item.StatusText = "Starting download…";
            OnDownloadStarted?.Invoke(item);

            var settings = SettingsService.Instance.Settings;
            var outputDir = settings.DownloadPath;
            Directory.CreateDirectory(outputDir);

            // Resolve unique filename
            var title = string.IsNullOrWhiteSpace(item.Title) || item.Title == "Analyzing…"
                ? "video" : item.Title;
            var ext = GetExtension(item.SelectedFormat, item.SelectedQuality);
            var outputPath = FileNameResolver.GetUniqueFilePath(outputDir, title, ext, _reservedPaths.Keys);
            reservedPath = outputPath;
            _reservedPaths[outputPath] = true;

            // Use yt-dlp's template but with our resolved filename base
            var outputTemplate = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(outputPath) + ".%(ext)s");

            var result = await wrapper.DownloadAsync(
                item.Url,
                outputTemplate,
                item.SelectedQuality,
                item.SelectedFormat,
                settings.ConcurrentFragments,
                onProgress: (percent, speed, eta, statusText, fragments) =>
                {
                    var now = DateTime.UtcNow;
                    bool shouldUpdate = false;
                    if (percent >= 100 || percent <= 0)
                    {
                        shouldUpdate = true;
                    }
                    else
                    {
                        _lastProgressUpdate.TryGetValue(item.Id, out var lastUpdate);
                        if ((now - lastUpdate).TotalMilliseconds >= 250)
                        {
                            shouldUpdate = true;
                            _lastProgressUpdate[item.Id] = now;
                        }
                    }

                    if (shouldUpdate)
                    {
                        item.Progress = percent;
                        item.Speed = speed;
                        item.Eta = eta;
                        item.StatusText = statusText;
                        item.Fragments = fragments;
                        OnProgressUpdated?.Invoke(item, percent, statusText);
                    }
                },
                referer: referer
            );

            _activeDownloads.TryRemove(item.Id, out _);
            _lastProgressUpdate.TryRemove(item.Id, out _);

            if (result == "success")
            {
                item.Status = DownloadStatus.Completed;
                item.Progress = 100;
                item.StatusText = "✅ Completed!";
                item.FilePath = outputPath;

                // Add to history
                StatsService.Instance.AddHistory(new HistoryEntry
                {
                    Title = item.Title,
                    Url = item.Url,
                    ThumbnailUrl = item.ThumbnailUrl,
                    Quality = item.SelectedQuality,
                    Format = item.SelectedFormat,
                    SizeMb = item.SizeMb,
                    FilePath = outputPath,
                });
            }
            else if (result == "cancelled")
            {
                item.Status = DownloadStatus.Cancelled;
                item.StatusText = "🚫 Cancelled";
                item.Progress = 0;
            }
            else
            {
                item.Status = DownloadStatus.Failed;
                item.StatusText = $"❌ {result[..Math.Min(result.Length, 100)]}";
            }

            OnDownloadCompleted?.Invoke(item, result);
        }
        finally
        {
            if (reservedPath != null)
            {
                _reservedPaths.TryRemove(reservedPath, out _);
            }
            _activeDownloads.TryRemove(item.Id, out _);
            _lastProgressUpdate.TryRemove(item.Id, out _);
            _concurrencySemaphore.Release();
        }
    }

    /// <summary>
    /// Download all items in the list, respecting concurrency.
    /// </summary>
    public async Task DownloadAllAsync(IEnumerable<DownloadItem> items, string? referer = null)
    {
        var tasks = items
            .Where(i => i.Status is DownloadStatus.Ready or DownloadStatus.Queued or DownloadStatus.Failed)
            .Select(item => StartDownloadAsync(item, referer))
            .ToList();

        await Task.WhenAll(tasks);
    }

    public void PauseItem(string itemId)
    {
        if (_activeDownloads.TryGetValue(itemId, out var entry))
        {
            entry.Wrapper.Pause();
            entry.Item.Status = DownloadStatus.Paused;
            entry.Item.StatusText = "⏸ Paused";
        }
    }

    public void ResumeItem(string itemId)
    {
        if (_activeDownloads.TryGetValue(itemId, out var entry))
        {
            entry.Wrapper.Resume();
            entry.Item.Status = DownloadStatus.Downloading;
            entry.Item.StatusText = "▶ Resuming…";
        }
    }

    public void CancelItem(string itemId)
    {
        if (_activeDownloads.TryGetValue(itemId, out var entry))
        {
            entry.Wrapper.Cancel();
            _activeDownloads.TryRemove(itemId, out _);
        }
    }

    public void PauseAll()
    {
        foreach (var entry in _activeDownloads.Values)
        {
            entry.Wrapper.Pause();
            entry.Item.Status = DownloadStatus.Paused;
            entry.Item.StatusText = "⏸ Paused";
        }
    }

    public void ResumeAll()
    {
        foreach (var entry in _activeDownloads.Values)
        {
            entry.Wrapper.Resume();
            entry.Item.Status = DownloadStatus.Downloading;
            entry.Item.StatusText = "▶ Resuming…";
        }
    }

    public void CancelAll()
    {
        foreach (var entry in _activeDownloads.Values)
        {
            entry.Wrapper.Cancel();
        }
        _activeDownloads.Clear();
    }

    private static string GetExtension(string format, string quality)
    {
        if (quality == "Audio Only")
            return format.ToLower() is "mp3" or "wav" or "m4a" ? format.ToLower() : "mp3";
        return format.ToLower() switch
        {
            "mp3" or "wav" or "m4a" => format.ToLower(),
            "mkv" => "mkv",
            _ => "mp4"
        };
    }
}
