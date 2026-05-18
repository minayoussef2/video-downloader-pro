using System.Windows;
using VideoDownloaderPro.Services;
using VideoDownloaderPro.ViewModels;

namespace VideoDownloaderPro;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = (MainViewModel)DataContext;

        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closing += OnClosing;

        SetupTrayIcon();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Video Downloader Pro",
            Visible = true,
        };

        // Use the custom icon from resources
        try
        {
            var iconUri = new System.Uri("pack://application:,,,/icon.ico");
            var resourceStream = System.Windows.Application.GetResourceStream(iconUri);
            if (resourceStream != null)
            {
                _trayIcon.Icon = new System.Drawing.Icon(resourceStream.Stream);
            }
            else
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application;
            }
        }
        catch 
        {
            _trayIcon.Icon = System.Drawing.SystemIcons.Application;
        }

        _trayIcon.DoubleClick += (_, _) =>
        {
            Show();
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            Activate();
        };

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => { Show(); WindowState = WindowState.Normal; ShowInTaskbar = true; });
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => { _trayIcon.Visible = false; _viewModel.Cleanup(); System.Windows.Application.Current.Shutdown(); });
        _trayIcon.ContextMenuStrip = menu;
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (SettingsService.Instance.Settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            ShowInTaskbar = false;
        }
        else
        {
            _trayIcon?.Dispose();
            _viewModel.Cleanup();
        }
    }
}