using System;
using System.Windows;
using Monity.Infrastructure.Persistence;

namespace Monity.App.Services;

public class ThemeService
{
    private const string SettingKey = "theme";
    private const string Light = "light";
    private const string Dark = "dark";

    private readonly IUsageRepository _repository;

    public ThemeService(IUsageRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Uygulama başlarken saklanan tema tercihini uygular.
    /// </summary>
    public async System.Threading.Tasks.Task ApplyStoredThemeAsync()
    {
        var theme = await _repository.GetSettingAsync(SettingKey) ?? Light;
        ApplyTheme(theme);
    }

    /// <summary>
    /// Verilen temayı uygular ve ayarlara kaydeder.
    /// </summary>
    public void ApplyTheme(string theme)
    {
        var normalized = theme?.Trim().ToLowerInvariant() switch
        {
            Dark => Dark,
            _ => Light
        };

        var uri = normalized == Dark
            ? new Uri("pack://application:,,,/Monity.App;component/Themes/Dark.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/Monity.App;component/Themes/Light.xaml", UriKind.Absolute);

        var dict = new ResourceDictionary { Source = uri };
        var app = (App)System.Windows.Application.Current;
        if (app.Resources.MergedDictionaries.Count > 0)
            app.Resources.MergedDictionaries[0] = dict;
        else
            app.Resources.MergedDictionaries.Add(dict);
    }

    /// <summary>
    /// Tema tercihini veritabanına kaydeder (tema değişikliği sırasında çağrılır).
    /// </summary>
    public System.Threading.Tasks.Task SaveThemeAsync(string theme)
    {
        var normalized = theme?.Trim().ToLowerInvariant() switch
        {
            Dark => Dark,
            _ => Light
        };
        return _repository.SetSettingAsync(SettingKey, normalized);
    }

    /// <summary>
    /// Mevcut tema tercihini döndürür.
    /// </summary>
    public async System.Threading.Tasks.Task<string> GetThemeAsync()
    {
        var theme = await _repository.GetSettingAsync(SettingKey) ?? Light;
        return theme.Trim().ToLowerInvariant() == Dark ? Dark : Light;
    }
}
