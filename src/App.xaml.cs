using System.Threading;
using System.Windows;
using System.Linq;
using System.Runtime.InteropServices;

namespace VideoDownloaderPro;

/// <summary>
/// Application entry point.
/// Enforces single-instance via a named Mutex + named EventWaitHandle.
/// If a second instance launches, it signals the existing one to restore its window.
/// </summary>
public partial class App : System.Windows.Application
{
    // ── Win32 Interop — Used to bring the existing instance's window to the foreground ──
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint hWnd, int nCmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hWnd);
    private const int SW_RESTORE = 9;

    // ── Single-Instance Primitives ──
    private static Mutex? _mutex;
    private static EventWaitHandle? _showEvent;
    private Thread? _listenerThread;

    /// <summary>
    /// Name of the cross-process event used to signal "show the main window".
    /// </summary>
    private const string ShowEventName = "VideoDownloaderPro_ShowEvent";
    private const string MutexName     = "VideoDownloaderPro_SingleInstance";

    protected override void OnStartup(StartupEventArgs e)
    {
        // ── Global crash handler — persists errors to crash_log.txt ──
        this.DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                var error = args.Exception?.ToString() ?? "Unknown Error";
                System.IO.File.WriteAllText("crash_log.txt", error);
                System.Windows.MessageBox.Show(
                    $"Application crashed! Details saved to crash_log.txt.\n\nError: {args.Exception?.Message}",
                    "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            args.Handled = true;
            System.Environment.Exit(1);
        };

        // ── Single-instance enforcement via named Mutex ──
        _mutex = new Mutex(true, MutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            // Another instance is already running — signal it to show its window, then exit.
            try
            {
                var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();  // Wake up the background listener thread in the running instance
                existing.Dispose();
            }
            catch
            {
                // Fallback: find the existing process and activate its main window via Win32
                ActivateExistingInstance();
            }

            Current.Shutdown();
            return;
        }

        base.OnStartup(e);
        this.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // ── Create the cross-process event so future instances can signal us ──
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        StartShowEventListener();

        // ── Create and show the main window ──
        bool startMinimized = e.Args.Contains("--minimized");

        var mainWindow = new MainWindow();
        if (startMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowInTaskbar = false;
            // Force HWND creation without rendering to prevent GDI/quota crash on Windows startup
            var helper = new System.Windows.Interop.WindowInteropHelper(mainWindow);
            helper.EnsureHandle();
        }
        else
        {
            mainWindow.Show();
        }
    }

    /// <summary>
    /// Background thread that waits for the named event to be signaled by a second instance.
    /// When signaled, it restores and activates the main window on the UI thread.
    /// </summary>
    private void StartShowEventListener()
    {
        _listenerThread = new Thread(() =>
        {
            while (_showEvent != null)
            {
                try
                {
                    // Block until another instance calls .Set()
                    _showEvent.WaitOne();

                    // Restore the main window on the dispatcher thread
                    Current?.Dispatcher.Invoke(() =>
                    {
                        var win = Current.MainWindow;
                        if (win != null)
                        {
                            win.Show();
                            win.ShowInTaskbar = true;
                            win.WindowState = WindowState.Normal;
                            win.Activate();
                            win.Focus();
                        }
                    });
                }
                catch (ObjectDisposedException)
                {
                    break; // App is shutting down
                }
                catch
                {
                    // Swallow transient errors and keep listening
                }
            }
        })
        {
            IsBackground = true,
            Name = "ShowEventListener"
        };
        _listenerThread.Start();
    }

    /// <summary>
    /// Win32 fallback: find the existing VideoDownloaderPro process and bring its window forward.
    /// </summary>
    private static void ActivateExistingInstance()
    {
        try
        {
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var processes = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);

            foreach (var proc in processes)
            {
                if (proc.Id != currentProcess.Id && proc.MainWindowHandle != nint.Zero)
                {
                    ShowWindow(proc.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(proc.MainWindowHandle);
                    break;
                }
            }
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Clean up single-instance resources
        try { _showEvent?.Set(); } catch { }      // Unblock the listener thread
        try { _showEvent?.Dispose(); } catch { }
        _showEvent = null;

        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();

        base.OnExit(e);
    }
}
