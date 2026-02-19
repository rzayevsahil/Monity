using System.Windows;
using Monity.Infrastructure.Persistence;

namespace Monity.App.Services;

public class LanguageService
{
    private const string SettingKey = "language";
    private const string Turkish = "tr";
    private const string English = "en";

    private readonly IUsageRepository _repository;

    public LanguageService(IUsageRepository repository)
    {
        _repository = repository;
    }

    public async System.Threading.Tasks.Task ApplyStoredLanguageAsync()
    {
        var lang = await _repository.GetSettingAsync(SettingKey) ?? Turkish;
        ApplyLanguage(lang);
    }

    public void ApplyLanguage(string language)
    {
        var normalized = language?.Trim().ToLowerInvariant() switch
        {
            English => English,
            _ => Turkish
        };

        var uri = normalized == English
            ? new Uri("pack://application:,,,/Monity.App;component/Resources/Strings.en.xaml", UriKind.Absolute)
            : new Uri("pack://application:,,,/Monity.App;component/Resources/Strings.tr.xaml", UriKind.Absolute);

        var dict = new ResourceDictionary { Source = uri };
        var app = (App)System.Windows.Application.Current;
        while (app.Resources.MergedDictionaries.Count < 2)
            app.Resources.MergedDictionaries.Add(new ResourceDictionary());
        app.Resources.MergedDictionaries[1] = dict;
    }

    public System.Threading.Tasks.Task SaveLanguageAsync(string language)
    {
        var normalized = language?.Trim().ToLowerInvariant() switch
        {
            English => English,
            _ => Turkish
        };
        return _repository.SetSettingAsync(SettingKey, normalized);
    }

    public async System.Threading.Tasks.Task<string> GetLanguageAsync()
    {
        var lang = await _repository.GetSettingAsync(SettingKey) ?? Turkish;
        return lang.Trim().ToLowerInvariant() == English ? English : Turkish;
    }
}
