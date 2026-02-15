using System.Diagnostics;

namespace Monity.Infrastructure;

/// <summary>
/// Resolves user-friendly display names from executable version info (FileDescription / ProductName).
/// </summary>
public static class AppDisplayNameResolver
{
    /// <summary>
    /// Tries to get a display name from the executable's version info.
    /// Returns FileDescription if set, otherwise ProductName, otherwise null.
    /// Never throws; returns null on missing file or access errors.
    /// </summary>
    public static string? GetDisplayNameFromExe(string? exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        try
        {
            if (!File.Exists(exePath))
                return null;

            var info = FileVersionInfo.GetVersionInfo(exePath);
            var name = info.FileDescription?.Trim();
            if (!string.IsNullOrEmpty(name))
                return name;
            name = info.ProductName?.Trim();
            if (!string.IsNullOrEmpty(name))
                return name;
            return null;
        }
        catch
        {
            return null;
        }
    }
}
