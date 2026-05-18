using Microsoft.Win32;
using Newtonsoft.Json;
using System.Diagnostics;

namespace VideoDownloaderPro.Services;

/// <summary>
/// Auto-installs the browser extension into Chrome/Edge/Firefox on startup.
/// Uses Registry keys for Chromium browsers and profile directory for Firefox.
/// </summary>
public static class ExtensionInstaller
{
    private const string ExtensionId = "videodownloaderpro@antigravity";

    /// <summary>
    /// Check if extension is installed and install if not.
    /// </summary>
    public static void EnsureInstalled()
    {
        try
        {
            var extensionDir = GetExtensionDirectory();
            if (!Directory.Exists(extensionDir))
            {
                Debug.WriteLine("Extension directory not found, skipping install.");
                return;
            }

            InstallForChromium("Google\\Chrome", extensionDir);
            InstallForChromium("Microsoft\\Edge", extensionDir);
            InstallForFirefox(extensionDir);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Extension install error: {ex.Message}");
        }
    }

    /// <summary>
    /// Check if the extension is registered for any browser.
    /// </summary>
    public static bool IsInstalled()
    {
        try
        {
            // Check Chrome
            using var chromeKey = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Google\Chrome\Extensions\" + ExtensionId);
            if (chromeKey != null) return true;

            // Check Edge
            using var edgeKey = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Edge\Extensions\" + ExtensionId);
            if (edgeKey != null) return true;

            return false;
        }
        catch { return false; }
    }

    private static string GetExtensionDirectory()
    {
        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        
        // 1. Try relative to the app directory (production)
        var extDir = Path.Combine(appDir, "extension");
        if (Directory.Exists(extDir)) return extDir;

        // 2. Try relative to the debug bin directory (development)
        // Usually bin\Debug\net8.0-windows\ -> so go up 4 levels
        var devDir = Path.GetFullPath(Path.Combine(appDir, "..", "..", "..", "..", "extension"));
        if (Directory.Exists(devDir)) return devDir;

        // 3. Current working directory fallback
        var cwdDir = Path.Combine(Directory.GetCurrentDirectory(), "extension");
        if (Directory.Exists(cwdDir)) return cwdDir;

        return extDir; // Return original path even if not found to prevent nulls, though it won't install
    }

    /// <summary>
    /// Register extension for Chromium-based browsers via Registry.
    /// This creates an "externally_connectable" extension entry.
    /// </summary>
    private static void InstallForChromium(string browserKey, string extensionDir)
    {
        try
        {
            var regPath = $@"SOFTWARE\{browserKey}\Extensions\{ExtensionId}";

            using var key = Registry.CurrentUser.CreateSubKey(regPath);
            if (key != null)
            {
                // Point to the unpacked extension directory
                key.SetValue("path", extensionDir);
                key.SetValue("version", "3.0.0");
                Debug.WriteLine($"Registered extension for {browserKey}");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register for {browserKey}: {ex.Message}");
        }
    }

    /// <summary>
    /// Install extension for Firefox by placing a pointer file in the profile extensions directory.
    /// </summary>
    private static void InstallForFirefox(string extensionDir)
    {
        try
        {
            var firefoxProfiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mozilla", "Firefox", "Profiles");

            if (!Directory.Exists(firefoxProfiles)) return;

            foreach (var profileDir in Directory.GetDirectories(firefoxProfiles, "*.default*"))
            {
                var extensionsDir = Path.Combine(profileDir, "extensions");
                Directory.CreateDirectory(extensionsDir);

                // Create a pointer file (Firefox native extension loading)
                var pointerFile = Path.Combine(extensionsDir, ExtensionId);
                if (!File.Exists(pointerFile))
                {
                    File.WriteAllText(pointerFile, extensionDir);
                    Debug.WriteLine($"Installed Firefox extension pointer in: {profileDir}");
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to install for Firefox: {ex.Message}");
        }
    }

    /// <summary>
    /// Remove extension registration from all browsers.
    /// </summary>
    public static void Uninstall()
    {
        try
        {
            // Remove Chromium registrations
            Registry.CurrentUser.DeleteSubKeyTree($@"SOFTWARE\Google\Chrome\Extensions\{ExtensionId}", false);
            Registry.CurrentUser.DeleteSubKeyTree($@"SOFTWARE\Microsoft\Edge\Extensions\{ExtensionId}", false);

            // Remove Firefox pointer files
            var firefoxProfiles = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Mozilla", "Firefox", "Profiles");

            if (Directory.Exists(firefoxProfiles))
            {
                foreach (var profileDir in Directory.GetDirectories(firefoxProfiles, "*.default*"))
                {
                    var pointerFile = Path.Combine(profileDir, "extensions", ExtensionId);
                    if (File.Exists(pointerFile)) File.Delete(pointerFile);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Extension uninstall error: {ex.Message}");
        }
    }
}
