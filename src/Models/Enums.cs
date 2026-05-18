namespace VideoDownloaderPro.Models;

public enum DownloadStatus
{
    Queued,
    Analyzing,
    Ready,
    Downloading,
    Paused,
    Completed,
    Failed,
    Cancelled
}

public enum DownloadSource
{
    App,
    Extension
}
