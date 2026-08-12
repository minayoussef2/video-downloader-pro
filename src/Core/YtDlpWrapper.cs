using System.Diagnostics;
using System.Text.RegularExpressions;
using VideoDownloaderPro.Models;

namespace VideoDownloaderPro.Core;

/// <summary>
/// Async wrapper around yt-dlp.exe process with real-time progress parsing.
/// Replaces the Python yt_dlp library with a process-based approach for stability.
/// </summary>
public class YtDlpWrapper
{
    private Process? _process;
    private bool _isCancelled;
    private bool _isPaused;
    private readonly ManualResetEventSlim _pauseEvent = new(true);

    // Quality → yt-dlp format string mapping
    private static readonly Dictionary<string, int> HeightMap = new()
    {
        ["8K (4320p)"] = 4320, ["4K (2160p)"] = 2160, ["2K (1440p)"] = 1440,
        ["1080p"] = 1080, ["720p"] = 720, ["480p"] = 480,
        ["360p"] = 360, ["240p"] = 240, ["144p"] = 144,
    };

    public bool IsPaused => _isPaused;
    public bool IsCancelled => _isCancelled;

    /// <summary>
    /// Find yt-dlp executable: bundled first, then PATH.
    /// </summary>
    private static string FindYtDlp()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var searchPaths = new[]
        {
            appDir,
            Path.Combine(appDir, "bin"),
            Path.Combine(appDir, "tools"),
            Path.Combine(appDir, "yt-dlp")
        };

        foreach (var dir in searchPaths)
        {
            if (!Directory.Exists(dir)) continue;
            var candidate = Path.Combine(dir, "yt-dlp.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // Check PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir.Trim(), "yt-dlp.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // Try winget/scoop/pip install locations
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var commonPaths = new[]
        {
            Path.Combine(userProfile, "AppData", "Local", "Microsoft", "WinGet", "Packages"),
            Path.Combine(userProfile, "scoop", "shims", "yt-dlp.exe"),
            Path.Combine(userProfile, "AppData", "Local", "Programs", "Python", "Python311", "Scripts", "yt-dlp.exe"),
            Path.Combine(userProfile, "AppData", "Local", "Programs", "Python", "Python312", "Scripts", "yt-dlp.exe"),
        };

        foreach (var p in commonPaths)
        {
            if (File.Exists(p)) return p;
        }

        return "yt-dlp"; // Fallback: let OS try to find it
    }

    /// <summary>
    /// Find ffmpeg executable.
    /// </summary>
    private static string? FindFfmpeg()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var searchPaths = new[]
        {
            appDir,
            Path.Combine(appDir, "bin"),
            Path.Combine(appDir, "tools"),
            Path.Combine(appDir, "ffmpeg"),
            Path.Combine(appDir, "ffmpeg", "bin")
        };

        foreach (var dir in searchPaths)
        {
            if (!Directory.Exists(dir)) continue;
            var candidate = Path.Combine(dir, "ffmpeg.exe");
            if (File.Exists(candidate)) return dir;
        }

        // Check PATH
        var pathDirs = Environment.GetEnvironmentVariable("PATH")?.Split(';') ?? Array.Empty<string>();
        foreach (var dir in pathDirs)
        {
            var candidate = Path.Combine(dir.Trim(), "ffmpeg.exe");
            if (File.Exists(candidate)) return dir.Trim();
        }

        return null;
    }

    /// <summary>
    /// Fetch video/playlist info without downloading.
    /// Returns parsed JSON output from yt-dlp.
    /// </summary>
    public async Task<string> GetInfoAsync(string url, bool flatPlaylist = false, string? referer = null)
    {
        var args = new List<string>
        {
            "--dump-json",
            "--no-download",
            "--no-warnings",
        };

        if (flatPlaylist)
        {
            args.Add("--flat-playlist");
        }

        if (!string.IsNullOrWhiteSpace(referer))
        {
            args.Add($"--referer \"{referer}\"");
        }

        args.Add($"\"{url}\"");

        var ytdlp = FindYtDlp();
        var psi = new ProcessStartInfo
        {
            FileName = ytdlp,
            Arguments = string.Join(" ", args), // Args are already quoted correctly in the list
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var ffmpegDir = FindFfmpeg();
        if (ffmpegDir != null)
            psi.EnvironmentVariables["PATH"] = ffmpegDir + ";" + Environment.GetEnvironmentVariable("PATH");

        using var proc = Process.Start(psi);
        if (proc == null) return "{\"error\": \"Failed to start yt-dlp\"}";

        var output = await proc.StandardOutput.ReadToEndAsync();
        var error = await proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();

        if (proc.ExitCode != 0 && string.IsNullOrWhiteSpace(output))
        {
            return $"{{\"error\": \"{EscapeJson(error.Trim())}\"}}";
        }

        return output;
    }

    /// <summary>
    /// Fetch available formats/qualities for a URL.
    /// Returns the raw output from yt-dlp -F.
    /// </summary>
    public async Task<List<string>> GetAvailableQualitiesAsync(string url, string? referer = null)
    {
        var qualities = new List<string> { "Best" };
        var ytdlp = FindYtDlp();

        var refererArg = string.IsNullOrWhiteSpace(referer) ? "" : $"--referer \"{referer}\" ";

        var psi = new ProcessStartInfo
        {
            FileName = ytdlp,
            Arguments = $"--dump-json --no-download --no-warnings {refererArg}\"{url}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc == null) return qualities;

            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            if (string.IsNullOrWhiteSpace(output)) return qualities;

            // Parse JSON to find available heights
            var heights = new HashSet<int>();
            bool hasAudio = false;

            // Parse format entries from JSON
            var heightMatches = Regex.Matches(output, @"""height""\s*:\s*(\d+)");
            foreach (Match m in heightMatches)
            {
                if (int.TryParse(m.Groups[1].Value, out int h) && h > 0)
                    heights.Add(h);
            }

            if (output.Contains("\"vcodec\": \"none\"") || output.Contains("\"acodec\":"))
                hasAudio = true;

            // Map heights to quality labels
            foreach (var kvp in HeightMap.OrderByDescending(x => x.Value))
            {
                if (heights.Any(h => h >= kvp.Value))
                    qualities.Add(kvp.Key);
            }

            if (hasAudio)
                qualities.Add("Audio Only");

            return qualities.Distinct().ToList();
        }
        catch
        {
            return qualities;
        }
    }

    /// <summary>
    /// Download a video with real-time progress reporting.
    /// </summary>
    public async Task<string> DownloadAsync(
        string url,
        string outputPath,
        string quality,
        string format,
        int concurrentFragments,
        Action<double, string, string, string, string>? onProgress = null,
        string? referer = null)
    {
        _isCancelled = false;
        _isPaused = false;
        _pauseEvent.Set();

        var ytdlp = FindYtDlp();
        var args = BuildDownloadArgs(url, outputPath, quality, format, concurrentFragments, referer);

        var psi = new ProcessStartInfo
        {
            FileName = ytdlp,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        var ffmpegDir = FindFfmpeg();
        if (ffmpegDir != null)
            psi.EnvironmentVariables["PATH"] = ffmpegDir + ";" + Environment.GetEnvironmentVariable("PATH");

        try
        {
            _process = Process.Start(psi);
            if (_process == null) return "Failed to start yt-dlp";

            // Read output in real-time for progress
            var errorOutput = new System.Text.StringBuilder();
            _ = Task.Run(async () =>
            {
                while (!_process.HasExited)
                {
                    var line = await _process.StandardError.ReadLineAsync();
                    if (line != null) errorOutput.AppendLine(line);
                }
            });

            while (!_process.HasExited)
            {
                // Check for pause
                _pauseEvent.Wait();

                if (_isCancelled)
                {
                    try { _process.Kill(entireProcessTree: true); } catch { }
                    return "cancelled";
                }

                var line = await _process.StandardOutput.ReadLineAsync();
                if (line == null) break;

                ParseProgressLine(line, onProgress);
            }

            await _process.WaitForExitAsync();

            if (_isCancelled) return "cancelled";

            if (_process.ExitCode == 0)
            {
                onProgress?.Invoke(100, "", "00:00", "Completed", "");
                return "success";
            }

            var err = errorOutput.ToString().Trim();
            // Clean up error messages
            foreach (var prefix in new[] { "ERROR: ", "yt_dlp.utils.DownloadError: ERROR: " })
            {
                if (err.StartsWith(prefix))
                    err = err[prefix.Length..];
            }

            return string.IsNullOrWhiteSpace(err) ? "Unknown error" : err;
        }
        catch (Exception ex) when (!_isCancelled)
        {
            return ex.Message;
        }
        finally
        {
            _process = null;
        }
    }

    private string BuildDownloadArgs(string url, string outputPath, string quality,
        string format, int concurrentFragments, string? referer)
    {
        var args = new List<string>();

        // FFmpeg location
        var ffmpegDir = FindFfmpeg();
        if (ffmpegDir != null)
        {
            args.Add($"--ffmpeg-location \"{ffmpegDir}\"");
        }

        // Format string
        string fmtStr;
        if (quality == "Audio Only" || format.ToLower() is "mp3" or "wav" or "m4a")
        {
            fmtStr = "bestaudio/best";
        }
        else if (HeightMap.TryGetValue(quality, out int h))
        {
            fmtStr = $"bestvideo[height<={h}]+bestaudio/best[height<={h}]/best";
        }
        else
        {
            fmtStr = "bestvideo+bestaudio/best";
        }

        args.Add($"-f \"{fmtStr}\"");
        args.Add($"-o \"{outputPath}\"");

        // Merge format
        if (format.ToLower() is "mp4" or "mkv")
            args.Add($"--merge-output-format {format.ToLower()}");

        // Audio post-processing
        if (format.ToLower() is "mp3" or "wav" or "m4a" || quality == "Audio Only")
        {
            var codec = format.ToLower() is "mp3" or "wav" or "m4a" ? format.ToLower() : "mp3";
            args.Add($"-x --audio-format {codec} --audio-quality 192K");
        }

        // Performance
        args.Add($"--concurrent-fragments {Math.Max(1, concurrentFragments)}");
        args.Add("--retries 10");
        args.Add("--fragment-retries 15");
        args.Add("--file-access-retries 5");
        args.Add("--socket-timeout 30");
        args.Add("--http-chunk-size 10485760");
        args.Add("--no-playlist");
        args.Add("--newline"); // Output progress on new lines for parsing

        // FIX: Force generic extractor and standard browser headers if we suspect AWS/DASH links
        if (url.Contains(".mpd") || url.Contains("index.mpd") || url.Contains("aws"))
        {
            args.Add("--force-generic-extractor");
        }
        
        // Always inject a standard desktop User-Agent to bypass CloudFront/AWS blocks
        args.Add("--user-agent \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36\"");

        // Referer for HLS/CDN
        if (!string.IsNullOrWhiteSpace(referer))
            args.Add($"--referer \"{referer}\"");

        args.Add($"\"{url}\"");

        return string.Join(" ", args);
    }

    // Progress parsing regex patterns
    private static readonly Regex ProgressRegex = new(
        @"\[download\]\s+(\d+\.?\d*)%\s+of\s+~?\s*(\S+)\s+at\s+(\S+)\s+ETA\s+(\S+)",
        RegexOptions.Compiled);

    private static readonly Regex FragmentRegex = new(
        @"\[download\]\s+.*?Fragment\s+(\d+)/(\d+)",
        RegexOptions.Compiled);

    private static readonly Regex SimpleProgressRegex = new(
        @"\[download\]\s+(\d+\.?\d*)%",
        RegexOptions.Compiled);

    private void ParseProgressLine(string line, Action<double, string, string, string, string>? onProgress)
    {
        if (onProgress == null) return;

        // Try full progress pattern: [download]  45.2% of ~500MiB at 12.5MiB/s ETA 00:35
        var match = ProgressRegex.Match(line);
        if (match.Success)
        {
            if (double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double percent))
            {
                var speed = match.Groups[3].Value;
                var eta = match.Groups[4].Value;
                var size = match.Groups[2].Value;
                onProgress(percent, speed, eta, $"⬇ {percent:F1}% of {size} at {speed} ETA {eta}", "");
            }
            return;
        }

        // Try fragment pattern
        var fragMatch = FragmentRegex.Match(line);
        if (fragMatch.Success)
        {
            if (int.TryParse(fragMatch.Groups[1].Value, out int frag) &&
                int.TryParse(fragMatch.Groups[2].Value, out int total) && total > 0)
            {
                double pct = (double)frag / total * 100;
                onProgress(pct, "", "", $"⬇ {pct:F1}%", $"{frag}/{total}");
            }
            return;
        }

        // Try simple percentage
        var simpleMatch = SimpleProgressRegex.Match(line);
        if (simpleMatch.Success)
        {
            if (double.TryParse(simpleMatch.Groups[1].Value, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out double pct))
            {
                onProgress(pct, "", "", $"⬇ {pct:F1}%", "");
            }
        }

        // Detect completion
        if (line.Contains("[Merger]") || line.Contains("Deleting original file") ||
            line.Contains("[download] 100%"))
        {
            onProgress(99.5, "", "", "Merging & finalizing…", "");
        }
    }

    [System.Runtime.InteropServices.DllImport("ntdll.dll", EntryPoint = "NtSuspendProcess")]
    private static extern int NtSuspendProcess(System.IntPtr processHandle);

    [System.Runtime.InteropServices.DllImport("ntdll.dll", EntryPoint = "NtResumeProcess")]
    private static extern int NtResumeProcess(System.IntPtr processHandle);

    public void Pause()
    {
        _isPaused = true;
        _pauseEvent.Reset();

        var proc = _process;
        if (proc != null)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        NtSuspendProcess(proc.Handle);
                    }
                }
                catch { }
            });
        }
    }

    public void Resume()
    {
        _isPaused = false;
        _pauseEvent.Set();

        var proc = _process;
        if (proc != null)
        {
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        NtResumeProcess(proc.Handle);
                    }
                }
                catch { }
            });
        }
    }

    public void Cancel()
    {
        _isCancelled = true;
        _pauseEvent.Set(); // Unblock if paused
        var proc = _process;
        if (proc != null)
        {
            // Resume first if suspended, so the process is awake to handle standard termination signals
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    if (!proc.HasExited)
                    {
                        NtResumeProcess(proc.Handle);
                    }
                }
                catch { }
                try { proc.Kill(entireProcessTree: true); } catch { }
            });
        }
    }

    private static string EscapeJson(string s)
        => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
}
