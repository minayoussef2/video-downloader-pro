namespace VideoDownloaderPro.Models;

public class AppSettings
{
    public string Theme { get; set; } = "Dark";
    public string DownloadPath { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "VideoDownloaderPro");
    public string DefaultQuality { get; set; } = "Best";
    public string DefaultFormat { get; set; } = "MP4";
    public int ConcurrentFragments { get; set; } = 4;
    public int MaxConcurrentDownloads { get; set; } = 1;
    public bool StartOnStartup { get; set; } = false;
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoInstallExtension { get; set; } = true;
    public int ExtensionServerPort { get; set; } = 18888;
}
