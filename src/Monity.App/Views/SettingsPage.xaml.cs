using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
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
    private readonly StartupService _startupService;
    private readonly ObservableCollection<AppExcludeItem> _appExcludeItems = [];
    private readonly ObservableCollection<DailyLimitItem> _dailyLimitItems = [];
    private readonly ObservableCollection<CategoryItem> _categoryItems = [];
    private readonly ICollectionView _appExcludeView;
    private readonly ICollectionView _dailyLimitView;
    private readonly ICollectionView _categoryView;
    private const int IdleMinSeconds = 10;
    private const int IdleMaxSeconds = 600;
    private const int MinSessionMaxSeconds = 600;
    private const int DailyLimitMinMinutes = 1;
    private const int DailyLimitMaxMinutes = 1440;
    private const string AppSearchPlaceholder = "Ara";
    private const string DailyLimitSearchPlaceholder = "Ara";
    private const string CategorySearchPlaceholder = "Ara";

    public SettingsPage(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _trackingService = services.GetRequiredService<UsageTrackingService>();
        _repository = services.GetRequiredService<IUsageRepository>();
        _themeService = services.GetRequiredService<ThemeService>();
        _startupService = services.GetRequiredService<StartupService>();
        _appExcludeView = CollectionViewSource.GetDefaultView(_appExcludeItems);
        _dailyLimitView = CollectionViewSource.GetDefaultView(_dailyLimitItems);
        _categoryView = CollectionViewSource.GetDefaultView(_categoryItems);
        AppExcludeList.ItemsSource = _appExcludeView;
        DailyLimitList.ItemsSource = _dailyLimitView;
        CategoryList.ItemsSource = _categoryView;
        CategoryComboBox.ItemsSource = CategoryItem.CategoryOptionsList;
        CategoryComboBox.DisplayMemberPath = "Display";
        CategoryComboBox.SelectedValuePath = "Value";
        CategoryComboBox.SelectedIndex = 0;
        System.Windows.DataObject.AddPastingHandler(TxtIdleThreshold, TxtIdleThreshold_OnPaste);
        System.Windows.DataObject.AddPastingHandler(TxtMinSessionSeconds, TxtMinSessionSeconds_OnPaste);
        System.Windows.DataObject.AddPastingHandler(TxtDailyLimitMinutes, TxtDailyLimitMinutes_OnPaste);
        Loaded += SettingsPage_Loaded;
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var theme = await _themeService.GetThemeAsync();
        RbThemeDark.IsChecked = theme == "dark";
        RbThemeLight.IsChecked = theme != "dark";

        var startWithWindows = await _startupService.GetIsEnabledAsync();
        CbStartWithWindows.IsChecked = startWithWindows;

        var idleSeconds = _trackingService.IdleThresholdMs / 1000;
        TxtIdleThreshold.Text = idleSeconds.ToString();

        TxtMinSessionSeconds.Text = _trackingService.MinSessionSeconds.ToString();

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

        // Günlük süre kısıtları listesi (takip hariç ile aynı kaynak; sütunda kısıtlı süre gösterilir)
        var limitsJson = await _repository.GetSettingAsync("daily_limits") ?? "{}";
        Dictionary<string, long>? limitsByProcess = null;
        try
        {
            limitsByProcess = JsonSerializer.Deserialize<Dictionary<string, long>>(limitsJson);
        }
        catch { /* invalid = no limits */ }

        _dailyLimitItems.Clear();
        foreach (var app in _appExcludeItems)
        {
            var limitSeconds = limitsByProcess != null && limitsByProcess.TryGetValue(app.ProcessName, out var sec) ? sec : (long?)null;
            var limitMinutes = limitSeconds.HasValue && limitSeconds > 0 ? (int)(limitSeconds.Value / 60) : (int?)null;
            _dailyLimitItems.Add(new DailyLimitItem(app.ProcessName, app.DisplayName, limitMinutes));
        }
        EmptyDailyLimitMessage.Visibility = _dailyLimitItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowDailyLimitSearchPlaceholder();
        ApplyDailyLimitSearchFilter();
        var limitExceededAction = await _repository.GetSettingAsync("limit_exceeded_action") ?? "notify";
        ChkLimitExceededCloseApp.IsChecked = limitExceededAction == "close_app";

        // Uygulama kategorileri listesi
        var appsWithCategory = await _repository.GetTrackedAppsWithCategoryAsync();
        _categoryItems.Clear();
        foreach (var app in appsWithCategory)
            _categoryItems.Add(new CategoryItem(app.AppId, app.DisplayName ?? app.ProcessName, app.CategoryName ?? ""));
        EmptyCategoryMessage.Visibility = _categoryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowCategorySearchPlaceholder();
        ApplyCategorySearchFilter();
    }

    private void TxtDailyLimitSearch_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TxtDailyLimitSearch.Text == DailyLimitSearchPlaceholder)
        {
            TxtDailyLimitSearch.Text = "";
            TxtDailyLimitSearch.SetResourceReference(ForegroundProperty, "TextBrush");
        }
    }

    private void TxtDailyLimitSearch_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDailyLimitSearch.Text))
            ShowDailyLimitSearchPlaceholder();
    }

    private void ShowDailyLimitSearchPlaceholder()
    {
        TxtDailyLimitSearch.Text = DailyLimitSearchPlaceholder;
        TxtDailyLimitSearch.SetResourceReference(ForegroundProperty, "TextMutedBrush");
    }

    private void TxtDailyLimitSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyDailyLimitSearchFilter();
    }

    private void ApplyDailyLimitSearchFilter()
    {
        var raw = TxtDailyLimitSearch?.Text ?? "";
        var query = (raw == DailyLimitSearchPlaceholder ? "" : raw).Trim();
        _dailyLimitView.Filter = string.IsNullOrEmpty(query)
            ? null
            : obj =>
            {
                if (obj is not DailyLimitItem item) return false;
                var ci = CultureInfo.CurrentCulture.CompareInfo;
                return ci.IndexOf(item.DisplayName, query, CompareOptions.IgnoreCase) >= 0
                       || ci.IndexOf(item.ProcessName, query, CompareOptions.IgnoreCase) >= 0;
            };
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

    private void BtnAppExcludeSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _appExcludeItems)
            item.IsExcluded = true;
    }

    private void BtnAppExcludeDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _appExcludeItems)
            item.IsExcluded = false;
    }

    private void BtnDailyLimitSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _dailyLimitItems)
            item.IsSelected = true;
    }

    private void BtnDailyLimitDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _dailyLimitItems)
            item.IsSelected = false;
    }

    private void TxtCategorySearch_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TxtCategorySearch.Text == CategorySearchPlaceholder)
        {
            TxtCategorySearch.Text = "";
            TxtCategorySearch.SetResourceReference(ForegroundProperty, "TextBrush");
        }
    }

    private void TxtCategorySearch_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtCategorySearch.Text))
            ShowCategorySearchPlaceholder();
    }

    private void ShowCategorySearchPlaceholder()
    {
        TxtCategorySearch.Text = CategorySearchPlaceholder;
        TxtCategorySearch.SetResourceReference(ForegroundProperty, "TextMutedBrush");
    }

    private void TxtCategorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyCategorySearchFilter();
    }

    private void ApplyCategorySearchFilter()
    {
        var raw = TxtCategorySearch?.Text ?? "";
        var query = (raw == CategorySearchPlaceholder ? "" : raw).Trim();
        _categoryView.Filter = string.IsNullOrEmpty(query)
            ? null
            : obj =>
            {
                if (obj is not CategoryItem item) return false;
                var ci = CultureInfo.CurrentCulture.CompareInfo;
                return ci.IndexOf(item.DisplayName, query, CompareOptions.IgnoreCase) >= 0;
            };
        _categoryView.Refresh();
    }

    private void BtnApplyCategory_Click(object sender, RoutedEventArgs e)
    {
        var selectedOption = CategoryComboBox.SelectedItem as CategoryOption;
        var categoryValue = selectedOption?.Value ?? "";
        foreach (var item in _categoryItems.Where(x => x.IsSelected))
            item.CategoryName = categoryValue;
    }

    private void BtnCategorySelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _categoryItems)
            item.IsSelected = true;
    }

    private void BtnCategoryDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _categoryItems)
            item.IsSelected = false;
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

    private void TxtMinSessionSeconds_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!Regex.IsMatch(e.Text, "^[0-9]+$")) { e.Handled = true; return; }
        var current = TxtMinSessionSeconds.Text ?? "";
        var start = Math.Min(TxtMinSessionSeconds.SelectionStart, current.Length);
        var len = TxtMinSessionSeconds.SelectionLength;
        var proposed = current[..Math.Max(0, start)] + e.Text + current[Math.Min(current.Length, start + len)..];
        if (proposed.Length > 0 && uint.TryParse(proposed, out var value) && value > MinSessionMaxSeconds)
            e.Handled = true;
    }

    private void TxtMinSessionSeconds_OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        var text = e.SourceDataObject.GetData(System.Windows.DataFormats.Text) as string;
        if (string.IsNullOrEmpty(text)) return;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { e.CancelCommand(); return; }
        e.CancelCommand();
        var current = TxtMinSessionSeconds.Text ?? "";
        var start = Math.Min(TxtMinSessionSeconds.SelectionStart, current.Length);
        var len = TxtMinSessionSeconds.SelectionLength;
        var newText = current[..Math.Max(0, start)] + digits + current[Math.Min(current.Length, start + len)..];
        if (uint.TryParse(newText, out var value) && value > MinSessionMaxSeconds)
            newText = MinSessionMaxSeconds.ToString();
        TxtMinSessionSeconds.Text = newText;
        TxtMinSessionSeconds.SelectionStart = TxtMinSessionSeconds.SelectionLength = 0;
        TxtMinSessionSeconds.SelectionStart = newText.Length;
    }

    private void TxtMinSessionSeconds_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtMinSessionSeconds.Text))
        {
            TxtMinSessionSeconds.Text = "0";
            return;
        }
        if (!uint.TryParse(TxtMinSessionSeconds.Text, out var value))
            return;
        if (value > MinSessionMaxSeconds)
            TxtMinSessionSeconds.Text = MinSessionMaxSeconds.ToString();
    }

    private void TxtDailyLimitMinutes_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!Regex.IsMatch(e.Text, "^[0-9]+$")) { e.Handled = true; return; }
        var current = TxtDailyLimitMinutes.Text ?? "";
        var start = Math.Min(TxtDailyLimitMinutes.SelectionStart, current.Length);
        var len = TxtDailyLimitMinutes.SelectionLength;
        var proposed = current[..Math.Max(0, start)] + e.Text + current[Math.Min(current.Length, start + len)..];
        if (proposed.Length > 0 && uint.TryParse(proposed, out var value) && value > DailyLimitMaxMinutes)
            e.Handled = true;
    }

    private void TxtDailyLimitMinutes_OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        var text = e.SourceDataObject.GetData(System.Windows.DataFormats.Text) as string;
        if (string.IsNullOrEmpty(text)) return;
        var digits = new string(text.Where(char.IsDigit).ToArray());
        if (digits.Length == 0) { e.CancelCommand(); return; }
        e.CancelCommand();
        var current = TxtDailyLimitMinutes.Text ?? "";
        var start = Math.Min(TxtDailyLimitMinutes.SelectionStart, current.Length);
        var len = TxtDailyLimitMinutes.SelectionLength;
        var newText = current[..Math.Max(0, start)] + digits + current[Math.Min(current.Length, start + len)..];
        if (uint.TryParse(newText, out var value) && value > DailyLimitMaxMinutes)
            newText = DailyLimitMaxMinutes.ToString();
        TxtDailyLimitMinutes.Text = newText;
        TxtDailyLimitMinutes.SelectionStart = TxtDailyLimitMinutes.SelectionLength = 0;
        TxtDailyLimitMinutes.SelectionStart = newText.Length;
    }

    private void TxtDailyLimitMinutes_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TxtDailyLimitMinutes.Text))
            return;
        if (!uint.TryParse(TxtDailyLimitMinutes.Text, out var value))
            return;
        if (value < DailyLimitMinMinutes)
            TxtDailyLimitMinutes.Text = DailyLimitMinMinutes.ToString();
        else if (value > DailyLimitMaxMinutes)
            TxtDailyLimitMinutes.Text = DailyLimitMaxMinutes.ToString();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        var frame = FindParent<Frame>(this);
        frame?.Navigate(new DashboardPage(_services));
    }

    private async void BtnDeleteOldData_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string tagStr || !int.TryParse(tagStr, out var days))
            return;
        var result = System.Windows.MessageBox.Show(
            $"{days} günden eski tüm kullanım verileri silinecektir. Bu işlem geri alınamaz. Emin misiniz?",
            "Veri Silme",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            var cutoff = DateTime.Today.AddDays(-days);
            await _repository.DeleteDataOlderThanAsync(cutoff);
            System.Windows.MessageBox.Show($"{days} günden eski veriler silindi.", "Monity", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Veri silinirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDeleteAll_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "Tüm kullanım verileri (oturumlar, özetler, uygulama kayıtları) silinecektir. Ayarlarınız korunacaktır. Bu işlem geri alınamaz. Emin misiniz?",
            "Tüm Verileri Sil",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            await _repository.DeleteAllDataAsync();
            System.Windows.MessageBox.Show("Tüm veriler silindi.", "Monity", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Veri silinirken hata oluştu: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnSaveGeneral_Click(object sender, RoutedEventArgs e)
    {
        if (!uint.TryParse(TxtIdleThreshold.Text, out var seconds) || seconds < IdleMinSeconds || seconds > IdleMaxSeconds)
        {
            System.Windows.MessageBox.Show("Boşta kalma süresi 10–600 saniye arasında olmalıdır.", "Hata", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _trackingService.IdleThresholdMs = seconds * 1000;
        await _repository.SetSettingAsync("idle_threshold_seconds", seconds.ToString());

        if (!uint.TryParse(TxtMinSessionSeconds.Text, out var minSession) || minSession > MinSessionMaxSeconds)
            minSession = 0;
        _trackingService.MinSessionSeconds = minSession;
        await _repository.SetSettingAsync("min_session_seconds", minSession.ToString());

        var theme = RbThemeDark.IsChecked == true ? "dark" : "light";
        _themeService.ApplyTheme(theme);
        await _themeService.SaveThemeAsync(theme);

        await _startupService.SetEnabledAsync(CbStartWithWindows.IsChecked == true);
        System.Windows.MessageBox.Show("Genel ayarlar kaydedildi.", "Monity", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnSaveAppExclude_Click(object sender, RoutedEventArgs e)
    {
        var ignored = string.Join(",", _appExcludeItems.Where(x => x.IsExcluded).Select(x => x.ProcessName));
        await _repository.SetSettingAsync("ignored_processes", ignored);
        var engine = _services.GetRequiredService<ITrackingEngine>();
        var userList = ignored.Length > 0 ? ignored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        engine.SetIgnoredProcesses(["Monity.App", "explorer"], userList);
        System.Windows.MessageBox.Show("Takip hariç listesi kaydedildi.", "Monity", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnSaveDailyLimit_Click(object sender, RoutedEventArgs e)
    {
        const int maxMinutesPerDay = 24 * 60;
        var inputText = (TxtDailyLimitMinutes?.Text ?? "").Trim();
        if (!string.IsNullOrEmpty(inputText) && long.TryParse(inputText, out var inputMinutes) && inputMinutes > 0)
        {
            if (inputMinutes > maxMinutesPerDay) inputMinutes = maxMinutesPerDay;
            var selectedMinutes = (int)inputMinutes;
            foreach (var item in _dailyLimitItems.Where(x => x.IsSelected))
                item.SetLimitMinutes(selectedMinutes);
        }
        var dailyLimits = new Dictionary<string, long>();
        foreach (var item in _dailyLimitItems)
        {
            if (item.LimitMinutes is not { } minutes || minutes <= 0) continue;
            var sec = minutes > maxMinutesPerDay ? maxMinutesPerDay * 60L : minutes * 60L;
            dailyLimits[item.ProcessName] = sec;
        }
        var dailyLimitsJson = JsonSerializer.Serialize(dailyLimits);
        await _repository.SetSettingAsync("daily_limits", dailyLimitsJson);
        var limitExceededAction = ChkLimitExceededCloseApp.IsChecked == true ? "close_app" : "notify";
        await _repository.SetSettingAsync("limit_exceeded_action", limitExceededAction);
        foreach (var item in _dailyLimitItems)
            item.IsSelected = false;
        System.Windows.MessageBox.Show("Günlük süre kısıtları kaydedildi.", "Monity", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnSaveCategory_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _categoryItems)
            await _repository.SetAppCategoryAsync(item.AppId, string.IsNullOrEmpty(item.CategoryName) ? null : item.CategoryName);
        System.Windows.MessageBox.Show("Uygulama kategorileri kaydedildi.", "Monity", MessageBoxButton.OK, MessageBoxImage.Information);
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

    private sealed class DailyLimitItem : INotifyPropertyChanged
    {
        private const int DisplayNameMaxLength = 28;

        public string ProcessName { get; }
        public string DisplayName { get; }
        /// <summary>Uygulama adı, tablo hizası için en fazla DisplayNameMaxLength karakter; uzunsa sonuna "…" eklenir.</summary>
        public string DisplayNameShort => string.IsNullOrEmpty(DisplayName)
            ? ""
            : DisplayName.Length <= DisplayNameMaxLength
                ? DisplayName
                : DisplayName[..(DisplayNameMaxLength - 1)] + "…";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        private int? _limitMinutes;
        public int? LimitMinutes
        {
            get => _limitMinutes;
            private set { _limitMinutes = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LimitMinutes))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LimitDisplayText))); }
        }
        public string LimitDisplayText => LimitMinutes is { } m && m > 0 ? $"{m} dk" : "—";
        public void SetLimitMinutes(int minutes) => LimitMinutes = minutes;
        public DailyLimitItem(string processName, string displayName, int? limitMinutes)
        {
            ProcessName = processName;
            DisplayName = displayName;
            _limitMinutes = limitMinutes;
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class CategoryItem : INotifyPropertyChanged
    {
        internal static readonly IReadOnlyList<CategoryOption> CategoryOptionsList =
        [
            new CategoryOption("", "Kategorisiz"),
            new CategoryOption("Diğer", "Diğer"),
            new CategoryOption("Tarayıcı", "Tarayıcı"),
            new CategoryOption("Geliştirme", "Geliştirme"),
            new CategoryOption("Sosyal", "Sosyal"),
            new CategoryOption("Eğlence", "Eğlence"),
            new CategoryOption("Ofis", "Ofis")
        ];
        public int AppId { get; }
        public string DisplayName { get; }
        private string _categoryName;
        public string CategoryName
        {
            get => _categoryName;
            set { _categoryName = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryName))); }
        }
        public IReadOnlyList<CategoryOption> CategoryOptions => CategoryOptionsList;
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
        }
        public CategoryItem(int appId, string displayName, string categoryName)
        {
            AppId = appId;
            DisplayName = displayName;
            _categoryName = categoryName ?? "";
        }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed record CategoryOption(string Value, string Display);
}
