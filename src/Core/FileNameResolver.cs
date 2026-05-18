using System.Text.RegularExpressions;

namespace VideoDownloaderPro.Core;

/// <summary>
/// Resolves filename collisions by appending (1), (2), etc. to duplicate names.
/// </summary>
public static class FileNameResolver
{
    private static readonly char[] InvalidChars = Path.GetInvalidFileNameChars();

    /// <summary>
    /// Sanitize a filename by replacing invalid characters with underscores.
    /// </summary>
    public static string Sanitize(string filename)
    {
        if (string.IsNullOrWhiteSpace(filename))
            return "video";

        var sanitized = new string(filename.Select(c =>
            InvalidChars.Contains(c) ? '_' : c).ToArray());

        // Trim dots and spaces from the end (Windows doesn't like those)
        sanitized = sanitized.TrimEnd('.', ' ');

        // Collapse multiple underscores
        sanitized = Regex.Replace(sanitized, @"_{2,}", "_");

        return string.IsNullOrWhiteSpace(sanitized) ? "video" : sanitized;
    }

    /// <summary>
    /// Returns a unique file path. If "video.mp4" exists, returns "video (1).mp4", etc.
    /// </summary>
    public static string GetUniqueFilePath(string directory, string filename, string extension)
    {
        var sanitizedName = Sanitize(filename);
        var ext = extension.StartsWith(".") ? extension : $".{extension}";

        var fullPath = Path.Combine(directory, $"{sanitizedName}{ext}");

        if (!File.Exists(fullPath))
            return fullPath;

        int counter = 1;
        while (true)
        {
            fullPath = Path.Combine(directory, $"{sanitizedName} ({counter}){ext}");
            if (!File.Exists(fullPath))
                return fullPath;
            counter++;

            // Safety valve
            if (counter > 9999)
            {
                fullPath = Path.Combine(directory,
                    $"{sanitizedName}_{Guid.NewGuid().ToString("N")[..6]}{ext}");
                return fullPath;
            }
        }
    }
}
