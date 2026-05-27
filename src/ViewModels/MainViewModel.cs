using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using Newtonsoft.Json.Linq;
using VideoDownloaderPro.Core;
using VideoDownloaderPro.Models;
using VideoDownloaderPro.Services;

namespace VideoDownloaderPro.ViewModels;

public class MainViewModel : ViewModelBase
{
    private readonly QueueManager _queueManager;
    private readonly ExtensionServer _extensionServer;
    private string _selectedTab = "Downloads";
    private string _statusText = "Idle — add URLs to start";
    private double _overallProgress;
    private string _urlInput = "";
    private string _hlsUrlInput = "";
    private string _hlsReferer = "";
    private string _globalQuality = "Best";
    private string _globalFormat = "MP4";
    private string _globalHlsQuality = "Best";
    private string _globalHlsFormat = "MP4";
    private bool _isAnalyzing;

    // Downloads tab
    public ObservableCollection<DownloadItem> DownloadQueue { get; } = new();
    public ObservableCollection<DownloadItem> HlsQueue { get; } = new();
    public ObservableCollection<HistoryEntry> History { get; } = new();

    // Settings
    public AppSettings Settings => SettingsService.Instance.Settings;

    // Properties
    public string SelectedTab { get => _selectedTab; set => SetField(ref _selectedTab, value); }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    public double OverallProgress { get => _overallProgress; set => SetField(ref _overallProgress, value); }
    public string UrlInput { get => _urlInput; set => SetField(ref _urlInput, value); }
    public string HlsUrlInput { get => _hlsUrlInput; set => SetField(ref _hlsUrlInput, value); }
    public string HlsReferer { get => _hlsReferer; set => SetField(ref _hlsReferer, value); }
    public string GlobalQuality { get => _globalQuality; set { if (SetField(ref _globalQuality, value)) ApplyGlobalQuality(false); } }
    public string GlobalFormat { get => _globalFormat; set { if (SetField(ref _globalFormat, value)) ApplyGlobalQuality(false); } }
    public string GlobalHlsQuality { get => _globalHlsQuality; set { if (SetField(ref _globalHlsQuality, value)) ApplyGlobalQuality(true); } }
    public string GlobalHlsFormat { get => _globalHlsFormat; set { if (SetField(ref _globalHlsFormat, value)) ApplyGlobalQuality(true); } }
    public bool IsAnalyzing { get => _isAnalyzing; set => SetField(ref _isAnalyzing, value); }

    public string QueueCount => $"{DownloadQueue.Count + HlsQueue.Count} Items";
    public string TotalSizeSum
    {
        get
        {
            long totalBytes = DownloadQueue.Sum(i => i.EstimatedSize) + HlsQueue.Sum(i => i.EstimatedSize);
            if (totalBytes <= 0) return "";
            double mb = totalBytes / 1024.0 / 1024.0;
            return mb > 1024 ? $"{mb / 1024.0:F2} GB" : $"{mb:F1} MB";
        }
    }
    private AppStats? _stats;
    public AppStats? Stats { get => _stats; set => SetField(ref _stats, value); }

    // Quality options for comboboxes
    public List<string> QualityOptions { get; } = new()
    {
        "Best", "8K (4320p)", "4K (2160p)", "2K (1440p)",
        "1080p", "720p", "480p", "360p", "240p", "144p", "Audio Only"
    };

    public List<string> FormatOptions { get; } = new() { "MP4", "MKV", "MP3", "WAV", "M4A" };

    // Commands
    public ICommand AddUrlsCommand { get; }
    public ICommand AddHlsUrlsCommand { get; }
    public ICommand DownloadAllCommand { get; }
    public ICommand HlsDownloadAllCommand { get; }
    public ICommand PauseAllCommand { get; }
    public ICommand CancelAllCommand { get; }
    public ICommand ClearAllCommand { get; }
    public ICommand DownloadItemCommand { get; }
    public ICommand PauseItemCommand { get; }
    public ICommand ResumeItemCommand { get; }
    public ICommand CancelItemCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand BrowsePathCommand { get; }
    public ICommand OpenDownloadFolderCommand { get; }
    public ICommand ClearHistoryCommand { get; }
    public ICommand RefreshHistoryCommand { get; }
    public ICommand RefreshStatsCommand { get; }

    public MainViewModel()
    {
        var settings = SettingsService.Instance.Settings;
        _globalQuality = settings.DefaultQuality;
        _globalFormat = settings.DefaultFormat;
        _globalHlsQuality = settings.DefaultQuality;
        _globalHlsFormat = settings.DefaultFormat;

        _queueManager = new QueueManager(settings.MaxConcurrentDownloads);

        // Extension server - Forcing 18888 to resolve stubborn port issues
        _extensionServer = new ExtensionServer(18888);
        _extensionServer.OnDownloadRequest += OnExtensionDownloadRequest;

        // Wire up queue manager events
        _queueManager.OnProgressUpdated += (item, pct, text) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(UpdateOverallProgress);
        };
        _queueManager.OnDownloadStarted += (item) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            });
        };
        _queueManager.OnDownloadCompleted += (item, result) =>
        {
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                UpdateOverallProgress();
                RefreshHistory();
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            });
        };

        // Commands
        AddUrlsCommand = new AsyncRelayCommand(AddUrlsAsync);
        AddHlsUrlsCommand = new AsyncRelayCommand(AddHlsUrlsAsync);
        DownloadAllCommand = new AsyncRelayCommand(DownloadAllAsync);
        HlsDownloadAllCommand = new AsyncRelayCommand(HlsDownloadAllAsync);
        PauseAllCommand = new RelayCommand(PauseAll);
        CancelAllCommand = new RelayCommand(CancelAll);
        ClearAllCommand = new RelayCommand(ClearAll);
        DownloadItemCommand = new RelayCommand(DownloadItem, CanDownloadItem);
        PauseItemCommand = new RelayCommand(PauseItem, CanPauseItem);
        ResumeItemCommand = new RelayCommand(ResumeItem, CanResumeItem);
        CancelItemCommand = new RelayCommand(CancelItem, CanCancelItem);
        RemoveItemCommand = new RelayCommand(RemoveItem);
        NavigateCommand = new RelayCommand(Navigate);
        SaveSettingsCommand = new RelayCommand(SaveSettings);
        BrowsePathCommand = new RelayCommand(BrowsePath);
        OpenDownloadFolderCommand = new RelayCommand(OpenDownloadFolder);
        ClearHistoryCommand = new RelayCommand(ClearHistory);
        RefreshHistoryCommand = new RelayCommand(_ => RefreshHistory());
        RefreshStatsCommand = new RelayCommand(_ => RefreshStats());

        // Load history/stats
        RefreshHistory();
        RefreshStats();
    }

    public async Task InitializeAsync()
    {
        // Start extension server
        await _extensionServer.StartAsync();

        // Auto-install extension if enabled
        if (Settings.AutoInstallExtension)
        {
            _ = Task.Run(() => ExtensionInstaller.EnsureInstalled());
        }
    }

    // ── URL Adding ──────────────────────────────────────────────────────────

    private async Task AddUrlsAsync(object? _)
    {
        var urls = ParseUrls(UrlInput);
        if (urls.Count == 0) return;

        UrlInput = "";
        StatusText = $"Adding {urls.Count} URL(s)...";

        foreach (var url in urls)
        {
            // Don't await here, let it run in background so UI updates immediately
            _ = AddSingleUrlAsync(url, DownloadQueue);
        }

        OnPropertyChanged(nameof(QueueCount));
    }

    private async Task AddHlsUrlsAsync(object? _)
    {
        var urls = ParseUrls(HlsUrlInput);
        if (urls.Count == 0) return;

        IsAnalyzing = true;
        StatusText = $"Analyzing {urls.Count} HLS URL(s)…";

        foreach (var url in urls)
        {
            await AddSingleUrlAsync(url, HlsQueue, isHls: true);
        }

        HlsUrlInput = "";
        IsAnalyzing = false;
        StatusText = $"HLS Queue: {HlsQueue.Count} item(s)";
        OnPropertyChanged(nameof(TotalSizeSum));
    }

    private async Task AddSingleUrlAsync(string url, ObservableCollection<DownloadItem> queue, bool isHls = false)
    {
        // Add immediate placeholder
        var item = new DownloadItem
        {
            Url = url,
            Referer = isHls ? HlsReferer : "",
            Title = "Detecting Video...",
            SelectedQuality = isHls ? GlobalHlsQuality : GlobalQuality,
            SelectedFormat = isHls ? GlobalHlsFormat : GlobalFormat,
            Status = DownloadStatus.Queued,
            StatusText = "Analyzing URL...",
        };

        System.Windows.Application.Current?.Dispatcher.Invoke(() => queue.Add(item));

        var wrapper = new YtDlpWrapper();

        try
        {
            var referer = isHls ? HlsReferer : null;
            var infoJson = await wrapper.GetInfoAsync(url, flatPlaylist: true, referer: referer);
            var lines = infoJson.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            if (lines.Length == 0) throw new Exception("No info returned");

            bool first = true;
            foreach (var line in lines)
            {
                try
                {
                    var json = JObject.Parse(line.Trim());
                    
                    if (first)
                    {
                        // Update the existing placeholder for the first video
                        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                        {
                            item.Url = json["webpage_url"]?.ToString() ?? json["url"]?.ToString() ?? url;
                            item.Title = json["title"]?.ToString() ?? "Unknown";
                            item.Uploader = json["uploader"]?.ToString() ?? json["channel"]?.ToString() ?? "";
                            item.ThumbnailUrl = json["thumbnail"]?.ToString() ?? "";
                            item.Duration = FormatDuration(json["duration"]?.Value<double?>());
                            item.ViewCount = json["view_count"]?.Value<long>() ?? 0;
                            item.Status = DownloadStatus.Ready;
                            item.StatusText = "Ready to download";

                            if (json["formats"] is JArray formats)
                            {
                                var qualities = ParseQualities(formats);
                                if (qualities.Count > 1) 
                                {
                                    item.AvailableQualities = qualities;
                                    if (!qualities.Contains(item.SelectedQuality))
                                    {
                                        item.SelectedQuality = qualities.Contains("Best") ? "Best" : (qualities.FirstOrDefault() ?? "Best");
                                    }
                                }
                                item.FormatSizes = ParseFormatSizes(formats, json["duration"]?.Value<double?>());
                                UpdateEstimatedSize(item);
                            }
                            OnPropertyChanged(nameof(TotalSizeSum));
                        });
                        first = false;
                    }
                    else
                    {
                        // Add new items if it's a playlist
                        var newItem = new DownloadItem
                        {
                            Url = json["webpage_url"]?.ToString() ?? json["url"]?.ToString() ?? url,
                            Title = json["title"]?.ToString() ?? "Unknown",
                            Uploader = json["uploader"]?.ToString() ?? json["channel"]?.ToString() ?? "",
                            ThumbnailUrl = json["thumbnail"]?.ToString() ?? "",
                            Duration = FormatDuration(json["duration"]?.Value<double?>()),
                            ViewCount = json["view_count"]?.Value<long>() ?? 0,
                            SelectedQuality = isHls ? GlobalHlsQuality : GlobalQuality,
                            SelectedFormat = isHls ? GlobalHlsFormat : GlobalFormat,
                            Status = DownloadStatus.Ready,
                            StatusText = "Ready to download",
                        };

                        if (json["formats"] is JArray formats)
                        {
                            var qualities = ParseQualities(formats);
                            if (qualities.Count > 1)
                            {
                                newItem.AvailableQualities = qualities;
                                if (!qualities.Contains(newItem.SelectedQuality))
                                {
                                    newItem.SelectedQuality = qualities.Contains("Best") ? "Best" : (qualities.FirstOrDefault() ?? "Best");
                                }
                            }
                            newItem.FormatSizes = ParseFormatSizes(formats, json["duration"]?.Value<double?>());
                            UpdateEstimatedSize(newItem);
                        }

                        System.Windows.Application.Current?.Dispatcher.Invoke(() => 
                        {
                            queue.Add(newItem);
                            OnPropertyChanged(nameof(TotalSizeSum));
                        });
                    }
                }
                catch { }
            }
        }
        catch (Exception)
        {
            // Update the placeholder with error info but keep it downloadable
            System.Windows.Application.Current?.Dispatcher.Invoke(() => 
            {
                item.Title = url.Length > 60 ? url[..60] + "…" : url;
                item.Status = DownloadStatus.Ready;
                item.StatusText = "Ready (Info fetch failed)";
            });
        }
    }

    // ── Download Controls ────────────────────────────────────────────────────

    private async Task DownloadAllAsync(object? _)
    {
        StatusText = $"Downloading {DownloadQueue.Count} item(s)…";
        _queueManager.SetMaxConcurrent(Settings.MaxConcurrentDownloads);
        await _queueManager.DownloadAllAsync(DownloadQueue);
        UpdateOverallProgress();
    }

    private async Task HlsDownloadAllAsync(object? _)
    {
        StatusText = $"Downloading {HlsQueue.Count} HLS item(s)…";
        _queueManager.SetMaxConcurrent(Settings.MaxConcurrentDownloads);
        await _queueManager.DownloadAllAsync(HlsQueue, referer: HlsReferer);
        UpdateOverallProgress();
    }

    private void DownloadItem(object? param)
    {
        if (param is not DownloadItem item) return;
        _queueManager.SetMaxConcurrent(Settings.MaxConcurrentDownloads);
        StatusText = $"Downloading: {item.Title}";

        var referer = HlsQueue.Contains(item) ? item.Referer : null;
        if (string.IsNullOrEmpty(referer) && HlsQueue.Contains(item)) referer = HlsReferer;
        
        _ = Task.Run(async () =>
        {
            await _queueManager.StartDownloadAsync(item, referer);
            System.Windows.Application.Current?.Dispatcher.Invoke(() =>
            {
                UpdateOverallProgress();
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            });
        });
    }

    private bool CanDownloadItem(object? param)
    {
        if (param is not DownloadItem item) return false;
        return item.Status is DownloadStatus.Ready or DownloadStatus.Failed or DownloadStatus.Cancelled;
    }

    private bool CanPauseItem(object? param)
    {
        if (param is not DownloadItem item) return false;
        return item.Status is DownloadStatus.Downloading;
    }

    private bool CanResumeItem(object? param)
    {
        if (param is not DownloadItem item) return false;
        return item.Status is DownloadStatus.Paused;
    }

    private bool CanCancelItem(object? param)
    {
        if (param is not DownloadItem item) return false;
        return item.Status is DownloadStatus.Downloading or DownloadStatus.Paused or DownloadStatus.Queued;
    }

    private void PauseItem(object? param)
    {
        if (param is DownloadItem item)
        {
            _queueManager.PauseItem(item.Id);
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private void ResumeItem(object? param)
    {
        if (param is DownloadItem item)
        {
            _queueManager.ResumeItem(item.Id);
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private void CancelItem(object? param)
    {
        if (param is DownloadItem item)
        {
            _queueManager.CancelItem(item.Id);
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private void RemoveItem(object? param)
    {
        if (param is DownloadItem item)
        {
            _queueManager.CancelItem(item.Id);
            DownloadQueue.Remove(item);
            HlsQueue.Remove(item);
            OnPropertyChanged(nameof(QueueCount));
            System.Windows.Input.CommandManager.InvalidateRequerySuggested();
        }
    }

    private void PauseAll(object? _) => _queueManager.PauseAll();
    private void CancelAll(object? _) => _queueManager.CancelAll();

    private void ClearAll(object? _)
    {
        _queueManager.CancelAll();
        DownloadQueue.Clear();
        HlsQueue.Clear();
        StatusText = "Idle — add URLs to start";
        OverallProgress = 0;
        OnPropertyChanged(nameof(QueueCount));
    }

    // ── Global Quality ───────────────────────────────────────────────────────

    private void ApplyGlobalQuality(bool isHls)
    {
        var activeQueue = isHls ? HlsQueue : DownloadQueue;
        var quality = isHls ? GlobalHlsQuality : GlobalQuality;
        var format = isHls ? GlobalHlsFormat : GlobalFormat;

        foreach (var item in activeQueue)
        {
            if (item.Status is DownloadStatus.Ready or DownloadStatus.Queued)
            {
                item.SelectedQuality = quality;
                item.SelectedFormat = format;
                UpdateEstimatedSize(item);
            }
        }
    }

    // ── Progress ─────────────────────────────────────────────────────────────

    private void UpdateOverallProgress()
    {
        var allItems = DownloadQueue.Concat(HlsQueue).ToList();
        if (allItems.Count == 0)
        {
            OverallProgress = 0;
            StatusText = "Idle — add URLs to start";
            return;
        }

        var totalProgress = allItems.Sum(i => i.Progress);
        OverallProgress = totalProgress / allItems.Count;

        var downloading = allItems.Count(i => i.Status == DownloadStatus.Downloading);
        var completed = allItems.Count(i => i.Status == DownloadStatus.Completed);
        var total = allItems.Count;

        if (completed == total)
        {
            StatusText = $"✅ All {total} downloads completed!";
            OverallProgress = 100;
        }
        else if (downloading > 0)
        {
            StatusText = $"⬇ Downloading {downloading}/{total} · {OverallProgress:F1}%";
        }
        else
        {
            StatusText = $"Queue: {total} items · {completed} completed";
        }
    }

    // ── Navigation ───────────────────────────────────────────────────────────

    private void Navigate(object? param)
    {
        if (param is string tab)
        {
            SelectedTab = tab;
            if (tab == "History") RefreshHistory();
            if (tab == "Stats") RefreshStats();
        }
    }

    // ── History & Stats ──────────────────────────────────────────────────────

    private void RefreshHistory()
    {
        History.Clear();
        foreach (var entry in StatsService.Instance.GetHistory())
            History.Add(entry);
    }

    private void RefreshStats()
    {
        Stats = StatsService.Instance.GetStats();
        OnPropertyChanged(nameof(Stats));
    }

    private void ClearHistory(object? _)
    {
        StatsService.Instance.ClearHistory();
        History.Clear();
    }

    // ── Settings ─────────────────────────────────────────────────────────────

    private void SaveSettings(object? _)
    {
        SettingsService.Instance.Save();
        StartupService.SetEnabled(Settings.StartOnStartup);
        _queueManager.SetMaxConcurrent(Settings.MaxConcurrentDownloads);
        
        System.Windows.MessageBox.Show("Settings saved successfully!", "Settings", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private void BrowsePath(object? _)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Download Folder"
        };
        if (dialog.ShowDialog() == true)
        {
            Settings.DownloadPath = dialog.FolderName;
            OnPropertyChanged(nameof(Settings));
        }
    }

    private void OpenDownloadFolder(object? _)
    {
        try
        {
            var path = Settings.DownloadPath;
            if (Directory.Exists(path))
                System.Diagnostics.Process.Start("explorer.exe", path);
        }
        catch { }
    }

    // ── Extension Integration ────────────────────────────────────────────────

    private void OnExtensionDownloadRequest(string url, string quality, string action, string type, string referer, string title, string thumbnail)
    {
        System.Windows.Application.Current?.Dispatcher.Invoke(async () =>
        {
            // Robust HLS/DASH/Direct detection
            string urlLower = url.ToLower();
            bool isHls = type == "HLS" || type == "DASH" || type == "MSS" || 
                         urlLower.Contains(".m3u8") || urlLower.Contains(".mpd") || urlLower.Contains(".isml") ||
                         urlLower.Contains("manifest") || urlLower.Contains("playlist");

            var item = new DownloadItem
            {
                Url = url,
                Referer = referer,
                Title = (string.IsNullOrWhiteSpace(title) || title == "Analyzing...") ? "Detecting Video..." : title,
                SelectedQuality = quality,
                SelectedFormat = isHls ? GlobalHlsFormat : GlobalFormat,
                Source = DownloadSource.Extension,
                Status = DownloadStatus.Queued,
                StatusText = "Fetching info...",
                ThumbnailUrl = thumbnail ?? ""
            };

            if (isHls)
            {
                HlsQueue.Add(item);
            }
            else
            {
                DownloadQueue.Add(item);
            }
            
            OnPropertyChanged(nameof(QueueCount));

            // Fetch info in background
            _ = Task.Run(async () =>
            {
                var wrapper = new YtDlpWrapper();
                try
                {
                    var infoJson = await wrapper.GetInfoAsync(url, referer: referer);
                    var json = JObject.Parse(infoJson);
                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        if (item.Title == "Analyzing..." || string.IsNullOrWhiteSpace(item.Title) || item.Title == "Detecting Video...")
                            item.Title = json["title"]?.ToString() ?? "Unknown";
                            
                        item.ThumbnailUrl = json["thumbnail"]?.ToString() ?? "";
                        item.Duration = FormatDuration((double?)json["duration"]);
                        item.Uploader = json["uploader"]?.ToString() ?? "";
                        
                        // Parse estimated size safely
                        if (json["formats"] is JArray formats)
                        {
                            var qualities = ParseQualities(formats);
                            if (qualities.Count > 1)
                            {
                                item.AvailableQualities = qualities;
                                if (!qualities.Contains(item.SelectedQuality))
                                {
                                    item.SelectedQuality = qualities.Contains("Best") ? "Best" : (qualities.FirstOrDefault() ?? "Best");
                                }
                            }
                            item.FormatSizes = ParseFormatSizes(formats, (double?)json["duration"]);
                            UpdateEstimatedSize(item);
                        }

                        item.Status = DownloadStatus.Ready;
                        item.StatusText = "Ready to download";
                        OnPropertyChanged(nameof(TotalSizeSum));
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    });
                }
                catch (Exception ex)
                {
                    try
                    {
                        System.IO.File.AppendAllText("server_log.txt", $"[{DateTime.Now}] Error fetching info for {url}: {ex}\n");
                    }
                    catch {}

                    System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    {
                        item.Status = DownloadStatus.Ready;
                        item.StatusText = "Ready (info unavailable)";
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested();
                    });
                }
            });

            // If action is "download", start immediately
            if (action == "download")
            {
                await Task.Delay(500); // Brief delay for info fetch
                if (DownloadItemCommand.CanExecute(item))
                {
                    DownloadItemCommand.Execute(item);
                }
            }

            // Navigate to appropriate tab
            SelectedTab = isHls ? "HLS" : "Downloads";
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<string> ParseUrls(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return new();
        
        // Support newlines, commas, and spaces as separators
        return input.Split(new[] { '\n', '\r', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(u => u.Trim())
            .Where(u => u.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .Distinct()
            .ToList();
    }

    private static string FormatDuration(double? seconds)
    {
        if (seconds is null or 0) return "—";
        var s = (int)seconds.Value;
        var h = s / 3600;
        var m = (s % 3600) / 60;
        var sec = s % 60;
        return h > 0 ? $"{h}:{m:D2}:{sec:D2}" : $"{m}:{sec:D2}";
    }

    private static List<string> ParseQualities(JArray formats)
    {
        var heights = new HashSet<int>();
        bool hasAudio = false;

        foreach (var fmt in formats)
        {
            var h = fmt["height"]?.Value<int?>() ?? 0;
            if (h > 0) heights.Add(h);
            if (fmt["vcodec"]?.ToString() == "none") hasAudio = true;
        }

        var qualities = new List<string> { "Best" };
        var map = new (string Label, int Height)[]
        {
            ("8K (4320p)", 4320), ("4K (2160p)", 2160), ("2K (1440p)", 1440),
            ("1080p", 1080), ("720p", 720), ("480p", 480),
            ("360p", 360), ("240p", 240), ("144p", 144),
        };

        foreach (var (label, height) in map)
        {
            if (heights.Any(h => h >= height))
                qualities.Add(label);
        }

        if (hasAudio) qualities.Add("Audio Only");
        return qualities;
    }

    private static Dictionary<string, long> ParseFormatSizes(JArray formats, double? duration)
    {
        var sizes = new Dictionary<string, long>();
        var heightMap = new Dictionary<string, int>
        {
            ["8K (4320p)"] = 4320, ["4K (2160p)"] = 2160, ["2K (1440p)"] = 1440,
            ["1080p"] = 1080, ["720p"] = 720, ["480p"] = 480,
            ["360p"] = 360, ["240p"] = 240, ["144p"] = 144,
        };

        foreach (var fmt in formats)
        {
            var h = fmt["height"]?.Value<int?>() ?? 0;
            var filesize = fmt["filesize"]?.Value<long?>() ?? fmt["filesize_approx"]?.Value<long?>();
            var tbr = fmt["tbr"]?.Value<double?>();

            if (filesize == null && tbr != null && duration > 0)
                filesize = (long)(tbr.Value * 1000 / 8 * duration.Value);

            if (filesize == null || filesize <= 0) continue;

            if (h == 0 && fmt["vcodec"]?.ToString() is "none" or "")
            {
                sizes["Audio Only"] = Math.Max(sizes.GetValueOrDefault("Audio Only"), filesize.Value);
                continue;
            }

            foreach (var (label, mapH) in heightMap)
            {
                if (h >= mapH)
                {
                    sizes[label] = Math.Max(sizes.GetValueOrDefault(label), filesize.Value);
                    break;
                }
            }
            sizes["Best"] = Math.Max(sizes.GetValueOrDefault("Best"), filesize.Value);
        }

        return sizes;
    }

    private static void UpdateEstimatedSize(DownloadItem item)
    {
        if (item.FormatSizes.TryGetValue(item.SelectedQuality, out long bytes) && bytes > 0)
        {
            item.EstimatedSize = bytes;
            item.SizeMb = bytes / (1024.0 * 1024.0);
        }
        else
        {
            item.EstimatedSize = 0;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "";
        if (bytes < 1024 * 1024) return $"≈ {bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"≈ {bytes / (1024.0 * 1024):F1} MB";
        return $"≈ {bytes / (1024.0 * 1024 * 1024):F2} GB";
    }

    private static int ParseQualityToHeight(string quality)
    {
        if (quality.Contains("4320p")) return 4320;
        if (quality.Contains("2160p")) return 2160;
        if (quality.Contains("1440p")) return 1440;
        if (quality.Contains("1080p")) return 1080;
        if (quality.Contains("720p")) return 720;
        if (quality.Contains("480p")) return 480;
        if (quality.Contains("360p")) return 360;
        if (quality.Contains("240p")) return 240;
        if (quality.Contains("144p")) return 144;
        return 0;
    }

    public void Cleanup()
    {
        _queueManager.CancelAll();
        _extensionServer.Dispose();
    }
}
