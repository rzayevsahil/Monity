using System.Windows;

namespace Monity.App.Helpers;

/// <summary>
/// Runtime dil kaynaklarından metin alır (app_settings: language).
/// </summary>
public static class Strings
{
    public static string Get(string key)
    {
        var value = System.Windows.Application.Current?.Resources[key];
        return value is string s ? s : key;
    }
}
