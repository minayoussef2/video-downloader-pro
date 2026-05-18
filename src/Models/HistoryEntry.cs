namespace VideoDownloaderPro.Models;

public class HistoryEntry
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public double SizeMb { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
}

public class AppStats
{
    public int TotalVideos { get; set; }
    public double TotalDataMb { get; set; }
    public int SessionVideos { get; set; }
    public double SessionDataMb { get; set; }
    public Dictionary<string, int> DailyStats { get; set; } = new();
}
