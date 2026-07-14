using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace VideoDownloaderPro.Models;

public class DownloadItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N")[..8];
    private string _url = string.Empty;
    private string _referer = string.Empty;
    private string _title = "Analyzing…";
    private string _uploader = string.Empty;
    private string _thumbnailUrl = string.Empty;
    private string _duration = "—";
    private long _viewCount;
    private string _selectedQuality = "Best";
    private string _selectedFormat = "MP4";
    private long _estimatedSize = 0;
    private double _progress;
    private string _speed = "";
    private string _eta = "";
    private string _statusText = "Queued";
    private DownloadStatus _status = DownloadStatus.Queued;
    private DownloadSource _source = DownloadSource.App;
    private List<string> _availableQualities = new() { "Best", "8K (4320p)", "4K (2160p)", "2K (1440p)", "1080p", "720p", "480p", "360p", "240p", "144p", "Audio Only" };
    private string _filePath = string.Empty;
    private double _sizeMb;
    private string _fragments = "";

    public string Id { get => _id; set => SetField(ref _id, value); }
    public string Url { get => _url; set => SetField(ref _url, value); }
    public string Referer { get => _referer; set => SetField(ref _referer, value); }
    public string Title { get => _title; set => SetField(ref _title, value); }
    public string Uploader { get => _uploader; set => SetField(ref _uploader, value); }
    public string ThumbnailUrl { get => _thumbnailUrl; set => SetField(ref _thumbnailUrl, value); }
    public string Duration { get => _duration; set => SetField(ref _duration, value); }
    public long ViewCount { get => _viewCount; set => SetField(ref _viewCount, value); }
    public string SelectedQuality { get => _selectedQuality; set => SetField(ref _selectedQuality, value); }
    public string SelectedFormat { get => _selectedFormat; set => SetField(ref _selectedFormat, value); }
    public long EstimatedSize { get => _estimatedSize; set => SetField(ref _estimatedSize, value); }
    public double Progress { get => _progress; set => SetField(ref _progress, value); }
    public string Speed { get => _speed; set => SetField(ref _speed, value); }
    public string Eta { get => _eta; set => SetField(ref _eta, value); }
    public string StatusText { get => _statusText; set => SetField(ref _statusText, value); }
    public DownloadStatus Status { get => _status; set => SetField(ref _status, value); }
    public DownloadSource Source { get => _source; set => SetField(ref _source, value); }
    public List<string> AvailableQualities { get => _availableQualities; set => SetField(ref _availableQualities, value); }
    public string FormattedSize
    {
        get
        {
            if (EstimatedSize <= 0) return "";
            if (EstimatedSize < 1024 * 1024) return $"≈ {EstimatedSize / 1024.0:F1} KB";
            if (EstimatedSize < 1024L * 1024 * 1024) return $"≈ {EstimatedSize / (1024.0 * 1024):F1} MB";
            return $"≈ {EstimatedSize / (1024.0 * 1024 * 1024):F2} GB";
        }
    }

    public string FilePath { get => _filePath; set => SetField(ref _filePath, value); }
    public double SizeMb { get => _sizeMb; set => SetField(ref _sizeMb, value); }
    public string Fragments { get => _fragments; set => SetField(ref _fragments, value); }

    private bool _isRenamingActive;
    private string _originalTitle = string.Empty;

    public bool IsRenamingActive { get => _isRenamingActive; set => SetField(ref _isRenamingActive, value); }
    public string OriginalTitle { get => _originalTitle; set => SetField(ref _originalTitle, value); }

    // Format size map: quality -> estimated bytes
    public Dictionary<string, long> FormatSizes { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
