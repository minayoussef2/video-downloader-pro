using Newtonsoft.Json;
using VideoDownloaderPro.Models;

namespace VideoDownloaderPro.Services;

/// <summary>
/// Manages application settings persistence using JSON config file.
/// </summary>
public class SettingsService
{
    private static readonly Lazy<SettingsService> _instance = new(() => new SettingsService());
    public static SettingsService Instance => _instance.Value;

    private readonly string _configPath;
    public AppSettings Settings { get; private set; }

    private SettingsService()
    {
        var appDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VideoDownloaderPro");
        Directory.CreateDirectory(appDir);
        _configPath = Path.Combine(appDir, "config.json");
        Settings = Load();

        // Ensure download directory exists
        try { Directory.CreateDirectory(Settings.DownloadPath); } catch { }
    }

    private AppSettings Load()
    {
        try
        {
            if (File.Exists(_configPath))
            {
                var json = File.ReadAllText(_configPath);
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
            }
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            var json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            File.WriteAllText(_configPath, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
        }
    }

    public string Get(string key)
    {
        return key switch
        {
            "Theme" => Settings.Theme,
            "DownloadPath" => Settings.DownloadPath,
            "DefaultQuality" => Settings.DefaultQuality,
            "DefaultFormat" => Settings.DefaultFormat,
            "ConcurrentFragments" => Settings.ConcurrentFragments.ToString(),
            "MaxConcurrentDownloads" => Settings.MaxConcurrentDownloads.ToString(),
            "StartOnStartup" => Settings.StartOnStartup.ToString(),
            "MinimizeToTray" => Settings.MinimizeToTray.ToString(),
            "AutoInstallExtension" => Settings.AutoInstallExtension.ToString(),
            "ExtensionServerPort" => Settings.ExtensionServerPort.ToString(),
            _ => string.Empty
        };
    }
}
