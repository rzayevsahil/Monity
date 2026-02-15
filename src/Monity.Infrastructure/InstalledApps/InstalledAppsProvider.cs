using Microsoft.Win32;

namespace Monity.Infrastructure.InstalledApps;

/// <summary>
/// Kurulu programlar listesini Windows Uninstall registry anahtarlarından okur.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class InstalledAppsProvider
{
    public record InstalledAppInfo(string DisplayName, string? ExePath);

    /// <summary>
    /// HKLM (64-bit + WOW6432 32-bit) ve HKCU Uninstall listesinden kurulu uygulamaları döner.
    /// Process name eşleştirmesi için ExePath mümkünse doldurulur.
    /// </summary>
    public static IReadOnlyList<InstalledAppInfo> GetInstalledApps()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<InstalledAppInfo>();

        try
        {
            // HKLM 64-bit
            AddFromHive(list, seen, RegistryHive.LocalMachine, RegistryView.Registry64);
            // HKLM 32-bit (WOW6432Node)
            AddFromHive(list, seen, RegistryHive.LocalMachine, RegistryView.Registry32);
            // HKCU
            AddFromHive(list, seen, RegistryHive.CurrentUser, RegistryView.Default);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Installed apps list could not be read from registry");
        }

        return list.OrderBy(x => x.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void AddFromHive(List<InstalledAppInfo> list, HashSet<string> seen,
        RegistryHive hive, RegistryView view)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (uninstall == null) return;

            foreach (var subKeyName in uninstall.GetSubKeyNames())
            {
                try
                {
                    using var subKey = uninstall.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var displayName = subKey.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName)) continue;
                    displayName = displayName.Trim();

                    // Sistem bileşenleri ve üst kurulum anahtarlarını atla
                    if (subKey.GetValue("SystemComponent") is int sys && sys != 0) continue;
                    if (subKey.GetValue("ParentKeyName") != null) continue;
                    if (subKey.GetValue("ParentDisplayName") != null && subKey.GetValue("SystemComponent") != null) continue;

                    var exePath = GetExePathFromKey(subKey);
                    var key = exePath ?? displayName;
                    if (seen.Contains(key)) continue;
                    seen.Add(key);

                    list.Add(new InstalledAppInfo(displayName, exePath));
                }
                catch
                {
                    // Tek bir alt anahtar hata verirse diğerlerine devam et
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Could not read Uninstall key for hive {Hive} view {View}", hive, view);
        }
    }

    private static string? GetExePathFromKey(RegistryKey subKey)
    {
        // DisplayIcon genelde "C:\...\app.exe,0" formatında
        var displayIcon = subKey.GetValue("DisplayIcon") as string;
        if (!string.IsNullOrWhiteSpace(displayIcon))
        {
            var path = displayIcon.Split(',')[0].Trim();
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && path.Length > 4)
                return path;
        }

        // InstallLocation + DisplayName veya yaygın exe adı denenebilir; basit tutuyoruz
        var installLocation = subKey.GetValue("InstallLocation") as string;
        if (!string.IsNullOrWhiteSpace(installLocation))
        {
            installLocation = installLocation.Trim();
            if (installLocation.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return installLocation;
            // Klasör ise DisplayName'den exe tahmin etmek güvenilir değil; null bırak
        }

        return null;
    }
}
