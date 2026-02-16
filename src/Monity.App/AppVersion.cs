using System.Reflection;

namespace Monity.App;

public static class AppVersion
{
    public static string Current => _current ??= GetInformationalVersion();

    private static string? _current;

    private static string GetInformationalVersion()
    {
        var attr = Assembly.GetEntryAssembly()
            ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        var raw = attr?.InformationalVersion?.Trim();
        if (string.IsNullOrEmpty(raw)) return "1.0.0";
        // 2.0.0+sha veya 2.0.0-beta -> sadece 2.0.0 (Version.TryParse için)
        var versionPart = raw.Split('-', '+')[0].Trim();
        return !string.IsNullOrEmpty(versionPart) ? versionPart : "1.0.0";
    }
}
