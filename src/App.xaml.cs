using System.Threading;
using System.Windows;
using System.Linq;

namespace VideoDownloaderPro;

public partial class App : System.Windows.Application
{
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Add global exception handling
        this.DispatcherUnhandledException += (s, args) =>
        {
            try
            {
                var error = args.Exception?.ToString() ?? "Unknown Error";
                System.IO.File.WriteAllText("crash_log.txt", error);
                System.Windows.MessageBox.Show($"Application crashed! Details saved to crash_log.txt.\n\nError: {args.Exception?.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch { }
            args.Handled = true;
            System.Environment.Exit(1);
        };

        // Single instance enforcement
        const string mutexName = "VideoDownloaderPro_SingleInstance";
        _mutex = new Mutex(true, mutexName, out bool isNewInstance);

        if (!isNewInstance)
        {
            System.Windows.MessageBox.Show("Video Downloader Pro is already running.",
                "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
            Current.Shutdown();
            return;
        }

        base.OnStartup(e);

        // Check if started minimized (from startup)
        bool startMinimized = e.Args.Contains("--minimized");

        var mainWindow = new MainWindow();
        if (startMinimized)
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.ShowInTaskbar = false;
        }
        else
        {
            mainWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
