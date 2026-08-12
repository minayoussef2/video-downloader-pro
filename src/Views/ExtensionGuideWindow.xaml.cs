using System.Diagnostics;
using System.IO;
using System.Windows;
using VideoDownloaderPro.Services;

namespace VideoDownloaderPro.Views;

public partial class ExtensionGuideWindow : Window
{
    public ExtensionGuideWindow()
    {
        InitializeComponent();
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var extPath = Path.Combine(baseDir, "extension");
            if (!Directory.Exists(extPath))
            {
                // Fallback to project root directory
                var parent = Directory.GetParent(baseDir)?.Parent?.Parent?.Parent?.FullName;
                if (parent != null)
                {
                    var projExt = Path.Combine(parent, "extension");
                    if (Directory.Exists(projExt)) extPath = projExt;
                }
            }

            if (Directory.Exists(extPath))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = extPath,
                    UseShellExecute = true
                });
            }
            else
            {
                System.Windows.MessageBox.Show($"Extension folder not found at:\n{extPath}", "Folder Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Could not open extension folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void DontShowAgain_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SettingsService.Instance.Settings.HasShownExtensionGuide = true;
            SettingsService.Instance.Save();
        }
        catch { }
        Close();
    }
}
