using System.Net;
using System.Text;
using Newtonsoft.Json;
using VideoDownloaderPro.Models;

namespace VideoDownloaderPro.Services;

/// <summary>
/// Lightweight HTTP server that listens for browser extension requests.
/// Runs on localhost:19999 to receive download URLs from the extension.
/// </summary>
public class ExtensionServer : IDisposable
{
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private readonly int _port;
    private int _actualPort;
    private bool _isRunning;

    /// <summary>
    /// Fired when the extension sends a download request.
    /// Parameters: url, quality, action, type, referer, title, thumbnail
    /// </summary>
    public event Action<string, string, string, string, string, string, string>? OnDownloadRequest;

    public event Action? OnServerStarted;
    private bool _isRetrying;

    public bool IsRunning => _isRunning;
    public int ActualPort => _actualPort;

    public ExtensionServer(int port = 18888)
    {
        _port = port;
        _actualPort = port;
    }

    public async Task StartAsync()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();

        // Try to bind immediately
        if (TryBind())
        {
            return;
        }

        // If it fails, start a background retry loop
        if (!_isRetrying)
        {
            _isRetrying = true;
            _ = Task.Run(() => RetryBindLoop(_cts.Token));
        }
    }

    private bool TryBind()
    {
        int startPort = _port;
        for (int i = 0; i < 5; i++)
        {
            int currentPort = startPort + i;
            var listener = new HttpListener();
            try
            {
                // Bind only to 127.0.0.1 to avoid dual-binding/localhost IPv6 conflicts
                listener.Prefixes.Add($"http://127.0.0.1:{currentPort}/");
                listener.Start();
                _listener = listener;
                _actualPort = currentPort;
                _isRunning = true;
                _isRetrying = false;
                System.IO.File.AppendAllText("server_log.txt", $"[{DateTime.Now}] Extension server started on port {_actualPort}\n");

                _ = Task.Run(() => ListenLoop(_cts!.Token));
                OnServerStarted?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText("server_log.txt", $"[{DateTime.Now}] Port {currentPort} bind failed: {ex.Message}\n");
                try { listener.Close(); } catch { }
                if (i == 4)
                {
                    System.IO.File.AppendAllText("server_log.txt", $"[{DateTime.Now}] ERROR: All fallback ports 18888-18892 failed.\n");
                    _isRunning = false;
                }
            }
        }
        return false;
    }

    private async Task RetryBindLoop(CancellationToken ct)
    {
        System.IO.File.AppendAllText("server_log.txt", $"[{DateTime.Now}] Starting background retry loop for port binding...\n");
        while (!ct.IsCancellationRequested && !_isRunning)
        {
            try
            {
                await Task.Delay(5000, ct);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            if (ct.IsCancellationRequested) break;

            if (TryBind())
            {
                System.IO.File.AppendAllText("server_log.txt", $"[{DateTime.Now}] Extension server successfully started during retry on port {_actualPort}\n");
                break;
            }
        }
        _isRetrying = false;
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener?.IsListening == true)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break; // Normal shutdown
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Server error: {ex.Message}");
            }
        }
    }

    private async Task HandleRequest(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // CORS headers for extension communication
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Access-Control-Allow-Private-Network");
        response.Headers.Add("Access-Control-Allow-Private-Network", "true");
        response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");

        try
        {
            // Handle preflight
            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 200;
                response.Close();
                return;
            }

            string responseBody;

            switch (request.Url?.AbsolutePath)
            {
                case "/status":
                    responseBody = JsonConvert.SerializeObject(new { status = "running", version = "1.0.0" });
                    break;

                case "/settings":
                    var settings = SettingsService.Instance.Settings;
                    responseBody = JsonConvert.SerializeObject(new { 
                        defaultQuality = settings.DefaultQuality, 
                        defaultFormat = settings.DefaultFormat 
                    });
                    break;

                case "/download":
                case "/queue":
                    if (request.HttpMethod == "POST")
                    {
                        using var reader = new System.IO.StreamReader(request.InputStream, System.Text.Encoding.UTF8);
                        var body = await reader.ReadToEndAsync();
                        var data = JsonConvert.DeserializeAnonymousType(body, new { url = "", quality = "Best", type = "Page", referer = "", title = "", thumbnail = "" });

                        if (!string.IsNullOrWhiteSpace(data?.url))
                        {
                            var action = request.Url.AbsolutePath == "/download" ? "download" : "queue";
                            OnDownloadRequest?.Invoke(data.url, data.quality ?? "Best", action, data.type ?? "Page", data.referer ?? "", data.title ?? "", data.thumbnail ?? "");
                            responseBody = JsonConvert.SerializeObject(new { success = true, message = $"URL {action}d" });
                        }
                        else
                        {
                            response.StatusCode = 400;
                            responseBody = JsonConvert.SerializeObject(new { error = "Missing url" });
                        }
                    }
                    else
                    {
                        response.StatusCode = 405;
                        responseBody = JsonConvert.SerializeObject(new { error = "POST required" });
                    }
                    break;

                default:
                    response.StatusCode = 404;
                    responseBody = JsonConvert.SerializeObject(new { error = "Not found" });
                    break;
            }

            var buffer = Encoding.UTF8.GetBytes(responseBody);
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Request handling error: {ex.Message}");
        }
        finally
        {
            try { response.Close(); } catch { }
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        _listener = null;
    }

    public void Dispose()
    {
        Stop();
        _cts?.Dispose();
    }
}
