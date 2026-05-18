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
    private bool _isRunning;

    /// <summary>
    /// Fired when the extension sends a download request.
    /// Parameters: url, quality, action, type, referer, title, thumbnail
    /// </summary>
    public event Action<string, string, string, string, string, string, string>? OnDownloadRequest;

    public bool IsRunning => _isRunning;

    public ExtensionServer(int port = 18888)
    {
        _port = port;
    }

    public async Task StartAsync()
    {
        if (_isRunning) return;

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        
        try
        {
            // Try 127.0.0.1 first as it's more reliable for extension communication
            _listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
            _listener.Prefixes.Add($"http://localhost:{_port}/");
            
            _listener.Start();
            _isRunning = true;
            System.IO.File.AppendAllText("server_log.txt", $"[{DateTime.Now}] Extension server started on port {_port}\n");

            _ = Task.Run(() => ListenLoop(_cts.Token));
        }
        catch (Exception ex)
        {
            System.IO.File.AppendAllText("server_log.txt", $"[{DateTime.Now}] ERROR starting server: {ex.Message}\n");
            _isRunning = false;
        }
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
