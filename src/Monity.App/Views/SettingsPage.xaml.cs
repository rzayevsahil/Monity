using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Monity.App.Views;
using Monity.App.Services;
using Monity.Infrastructure.InstalledApps;
using Monity.Infrastructure.Persistence;
using Monity.Infrastructure.Tracking;

namespace Monity.App.Views;

public partial class SettingsPage : Page
{
    private readonly IServiceProvider _services;
    private readonly UsageTrackingService _trackingService;
    private readonly IUsageRepository _repository;
    private readonly ThemeService _themeService;
    private readonly ObservableCollection<AppExcludeItem> _appExcludeItems = [];
    private readonly ICollectionView _appExcludeView;
    private const int IdleMinSeconds = 10;
    private const int IdleMaxSeconds = 600;
    private const string AppSearchPlaceholder = "Ara";

    public SettingsPage(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _trackingService = services.GetRequiredService<UsageTrackingService>();
        _repository = services.GetRequiredService<IUsageRepository>();
        _themeService = services.GetRequiredService<ThemeService>();
        _appExcludeView = CollectionViewSource.GetDefaultView(_appExcludeItems);
        AppExcludeList.ItemsSource = _appExcludeView;
        System.Windows.DataObject.AddPastingHandler(TxtIdleThreshold, TxtIdleThreshold_OnPaste);
        Loaded += SettingsPage_Loaded;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var theme = await _themeService.GetThemeAsync();
        RbThemeDark.IsChecked = theme == "dark";
        RbThemeLight.IsChecked = theme != "dark";

        var idleSeconds = _trackingService.IdleThresholdMs / 1000;
        TxtIdleThreshold.Text = idleSeconds.ToString();

        var ignoredStr = await _repository.GetSettingAsync("ignored_processes") ?? "";
        var ignoredSet = new HashSet<string>(ignoredStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);

        var apps = await _repository.GetTrackedAppsAsync();
        var installedApps = await Task.Run(InstalledAppsProvider.GetInstalledApps);

        _appExcludeItems.Clear();
        var processNamesAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Önce DB'de kayıtlı (daha önce kullanılmış) uygulamalar
        foreach (var app in apps)
        {
            var name = app.DisplayName ?? app.ProcessName;
            _appExcludeItems.Add(new AppExcludeItem(app.ProcessName, name, ignoredSet.Contains(app.ProcessName)));
            processNamesAdded.Add(app.ProcessName);
        }

        // Kurulu ama henüz DB'de olmayan uygulamaları ekle (registry'den)
        foreach (var installed in installedApps)
        {
            var processName = !string.IsNullOrWhiteSpace(installed.ExePath)
                ? Path.GetFileNameWithoutExtension(installed.ExePath)
                : installed.DisplayName.Trim();
            if (string.IsNullOrEmpty(processName)) continue;
            if (processNamesAdded.Contains(processName)) continue;
            processNamesAdded.Add(processName);
            _appExcludeItems.Add(new AppExcludeItem(processName, installed.DisplayName, ignoredSet.Contains(processName)));
        }

        EmptyAppListMessage.Visibility = _appExcludeItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowAppSearchPlaceholder();
        ApplyAppSearchFilter();
    }

    private void TxtAppSearch_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TxtAppSearch.Text == AppSearchPlaceholder)
        {
            TxtAppSearch.Text = "";
            TxtAppSearch.SetResourceReference(ForegroundProperty, "TextBrush");
        }
    }

    private void TxtAppSearch_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtAppSearch.Text))
            ShowAppSearchPlaceholder();
    }

    private void ShowAppSearchPlaceholder()
    {
        TxtAppSearch.Text = AppSearchPlaceholder;
        TxtAppSearch.SetResourceReference(ForegroundProperty, "TextMutedBrush");
    }

    private void TxtAppSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyAppSearchFilter();
    }

    private void ApplyAppSearchFilter()
    {
        var raw = TxtAppSearch?.Text ?? "";
        var query = (raw == AppSearchPlaceholder ? "" : raw).Trim();
        _appExcludeView.Filter = string.IsNullOrEmpty(query)
            ? null
            : obj =>
            {
                if (obj is not AppExcludeItem item) return false;
                var ci = CultureInfo.CurrentCulture.CompareInfo;
                return ci.IndexOf(item.DisplayName, query, CompareOptions.IgnoreCase) >= 0
                       || ci.IndexOf(item.ProcessName, query, CompareOptions.IgnoreCase) >= 0;
            };
    }

    private void TxtIdleThreshold_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!Regex.IsMatch(e.Text, "^[0-9]+$")) { e.Handled = true; return; }
        var current = TxtIdleThreshold.Text ?? "";
        var start = Math.Min(TxtIdleThreshold.SelectionStart, current.Length);
        var len = TxtIdleThreshold.SelectionLength;
        var proposed = current[..Math.Max(0, start)] + e.Text + current[Math.Min(current.Length, start + len)..];
        if (proposed.Length > 0 && uint.TryParse(proposed, out var value) && value > IdleMaxSeconds)
            e.Handled = true;
    }

    private void TxtIdleThreshold_OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        var text = e.SourceDataObject.GetData(System.Windows.DataFormats.Text) as string;
        if (string.IsNullOrEmpty(text)) return;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { e.CancelCommand(); return; }
        e.CancelCommand();
        var current = TxtIdleThreshold.Text ?? "";
        var start = Math.Min(TxtIdleThreshold.SelectionStart, current.Length);
        var len = TxtIdleThreshold.SelectionLength;
        var newText = current[..Math.Max(0, start)] + digits + current[Math.Min(current.Length, start + len)..];
        if (uint.TryParse(newText, out var value) && value > IdleMaxSeconds)
            newText = IdleMaxSeconds.ToString();
        TxtIdleThreshold.Text = newText;
        TxtIdleThreshold.SelectionStart = TxtIdleThreshold.SelectionLength = 0;
        TxtIdleThreshold.SelectionStart = newText.Length;
    }

    private void TxtIdleThreshold_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtIdleThreshold.Text))
        {
            TxtIdleThreshold.Text = "60";
            return;
        }
        if (!uint.TryParse(TxtIdleThreshold.Text, out var value))
            return;
        if (value < IdleMinSeconds)
            TxtIdleThreshold.Text = IdleMinSeconds.ToString();
        else if (value > IdleMaxSeconds)
            TxtIdleThreshold.Text = IdleMaxSeconds.ToString();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        var frame = FindParent<Frame>(this);
        frame?.Navigate(new DashboardPage(_services));
    }

    private async void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (!uint.TryParse(TxtIdleThreshold.Text, out var seconds) || seconds < IdleMinSeconds || seconds > IdleMaxSeconds)
        {
            System.Windows.MessageBox.Show("Idle eşiği 10-600 saniye arasında olmalıdır.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _trackingService.IdleThresholdMs = seconds * 1000;
        await _repository.SetSettingAsync("idle_threshold_seconds", seconds.ToString());

        var theme = RbThemeDark.IsChecked == true ? "dark" : "light";
        _themeService.ApplyTheme(theme);
        await _themeService.SaveThemeAsync(theme);

        var ignored = string.Join(",", _appExcludeItems.Where(x => x.IsExcluded).Select(x => x.ProcessName));
        await _repository.SetSettingAsync("ignored_processes", ignored);

        var engine = _services.GetRequiredService<ITrackingEngine>();
        var userList = ignored.Length > 0 ? ignored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        engine.SetIgnoredProcesses(["Monity.App", "explorer"], userList);

        System.Windows.MessageBox.Show("Ayarlar kaydedildi.", "Monity", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        var parent = VisualTreeHelper.GetParent(child);
        while (parent != null)
        {
            if (parent is T t)
                return t;
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    private sealed class AppExcludeItem : INotifyPropertyChanged
    {
        public string ProcessName { get; }
        public string DisplayName { get; }
        private bool _isExcluded;
        public bool IsExcluded
        {
            get => _isExcluded;
            set { _isExcluded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExcluded))); }
        }
        public AppExcludeItem(string processName, string displayName, bool isExcluded)
        {
            ProcessName = processName;
            DisplayName = displayName;
            _isExcluded = isExcluded;
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
