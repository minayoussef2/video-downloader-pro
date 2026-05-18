using Newtonsoft.Json;
using VideoDownloaderPro.Models;

namespace VideoDownloaderPro.Services;

/// <summary>
/// Manages download history and statistics with JSON persistence.
/// </summary>
public class StatsService
{
    private static readonly Lazy<StatsService> _instance = new(() => new StatsService());
    public static StatsService Instance => _instance.Value;

    private readonly string _dataDir;
    private readonly string _historyPath;
    private readonly string _statsPath;

    private List<HistoryEntry> _history;
    private AppStats _stats;

    private StatsService()
    {
        _dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoDownloaderPro", "data");
        Directory.CreateDirectory(_dataDir);

        _historyPath = Path.Combine(_dataDir, "history.json");
        _statsPath = Path.Combine(_dataDir, "stats.json");

        _history = LoadJson<List<HistoryEntry>>(_historyPath) ?? new List<HistoryEntry>();
        _stats = LoadJson<AppStats>(_statsPath) ?? new AppStats();

        // Reset session stats on startup
        _stats.SessionVideos = 0;
        _stats.SessionDataMb = 0;
    }

    private T? LoadJson<T>(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                return JsonConvert.DeserializeObject<T>(json);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading {path}: {ex.Message}");
        }
        return default;
    }

    private void SaveJson(string path, object data)
    {
        try
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving {path}: {ex.Message}");
        }
    }

    public void AddHistory(HistoryEntry entry)
    {
        entry.Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        _history.Insert(0, entry);
        SaveJson(_historyPath, _history);

        _stats.TotalVideos++;
        _stats.SessionVideos++;
        _stats.TotalDataMb += entry.SizeMb;
        _stats.SessionDataMb += entry.SizeMb;

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        _stats.DailyStats.TryGetValue(today, out int count);
        _stats.DailyStats[today] = count + 1;

        SaveJson(_statsPath, _stats);
    }

    public List<HistoryEntry> GetHistory()
    {
        _history = LoadJson<List<HistoryEntry>>(_historyPath) ?? new List<HistoryEntry>();
        return _history;
    }

    public AppStats GetStats()
    {
        _stats = LoadJson<AppStats>(_statsPath) ?? _stats;
        return _stats;
    }

    public void ClearHistory()
    {
        _history.Clear();
        SaveJson(_historyPath, _history);
    }
}
