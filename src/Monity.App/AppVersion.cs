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
        var v = attr?.InformationalVersion?.Split('-')[0].Trim();
        return !string.IsNullOrEmpty(v) ? v : "1.0.0";
    }
}
