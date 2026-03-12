using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Data;
using System.Windows;
using Monity.Domain.Entities;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Monity.App.Helpers;
using Monity.App.Views;
using Monity.App.Services;
using Monity.Domain.Entities;
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
    private readonly LanguageService _languageService;
    private readonly StartupService _startupService;
    private readonly IGoalService _goalService;
    private readonly ObservableCollection<AppExcludeItem> _appExcludeItems = [];
    private readonly ObservableCollection<DailyLimitItem> _dailyLimitItems = [];
    private readonly ObservableCollection<CategoryItem> _categoryItems = [];
    private readonly ObservableCollection<FocusModeBlockItem> _focusModeBlockedItems = [];
    private readonly ObservableCollection<Goal> _goals = [];
    private readonly ICollectionView _appExcludeView;
    private readonly ICollectionView _dailyLimitView;
    private readonly ICollectionView _categoryView;
    private const int IdleMinSeconds = 10;
    private const int IdleMaxSeconds = 600;
    private const int MinSessionMaxSeconds = 600;
    private const int DailyLimitMinMinutes = 1;
    private const int DailyLimitMaxMinutes = 1440;

    public SettingsPage(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _trackingService = services.GetRequiredService<UsageTrackingService>();
        _repository = services.GetRequiredService<IUsageRepository>();
        _themeService = services.GetRequiredService<ThemeService>();
        _languageService = services.GetRequiredService<LanguageService>();
        _startupService = services.GetRequiredService<StartupService>();
        _goalService = services.GetRequiredService<IGoalService>();
        _appExcludeView = CollectionViewSource.GetDefaultView(_appExcludeItems);
        _dailyLimitView = CollectionViewSource.GetDefaultView(_dailyLimitItems);
        _categoryView = CollectionViewSource.GetDefaultView(_categoryItems);
        AppExcludeList.ItemsSource = _appExcludeView;
        DailyLimitList.ItemsSource = _dailyLimitView;
        FocusModeBlockList.ItemsSource = _focusModeBlockedItems;
        CategoryList.ItemsSource = _categoryView;
        GoalsList.ItemsSource = _goals;
        CategoryComboBox.ItemsSource = CategoryItem.GetCategoryOptions();
        CategoryComboBox.DisplayMemberPath = "Display";
        CategoryComboBox.SelectedValuePath = "Value";
        CategoryComboBox.SelectedIndex = 0;
        System.Windows.DataObject.AddPastingHandler(TxtIdleThreshold, TxtIdleThreshold_OnPaste);
        System.Windows.DataObject.AddPastingHandler(TxtMinSessionSeconds, TxtMinSessionSeconds_OnPaste);
        System.Windows.DataObject.AddPastingHandler(TxtDailyLimitMinutes, TxtDailyLimitMinutes_OnPaste);
        CmbReportHour.ItemsSource = Enumerable.Range(0, 24).Select(i => i.ToString("D2")).ToList();
        CmbReportMinute.ItemsSource = Enumerable.Range(0, 60).Select(i => i.ToString("D2")).ToList();
        Loaded += SettingsPage_Loaded;

        // Initialize Goal Targets
        PopulateGoalComboBoxes();
        _ = PopulateGoalTargetsAsync();
    }

    private void PopulateGoalComboBoxes()
    {
        var prevFreq = CmbGoalFrequency.SelectedValue;
        var prevLimit = CmbGoalLimitType.SelectedValue;
        var prevUnit = CmbGoalUnit.SelectedValue;
        var prevTarget = CmbGoalTargetType.SelectedValue;

        // Frequency
        CmbGoalFrequency.ItemsSource = new List<ComboBoxOption>
        {
            new("Daily", (string)FindResource("Settings_Frequency_Daily")),
            new("Weekly", (string)FindResource("Settings_Frequency_Weekly")),
            new("Monthly", (string)FindResource("Settings_Frequency_Monthly"))
        };
        if (prevFreq != null) CmbGoalFrequency.SelectedValue = prevFreq;
        else CmbGoalFrequency.SelectedIndex = 0;

        // Limit Type
        CmbGoalLimitType.ItemsSource = new List<ComboBoxOption>
        {
            new("Max", (string)FindResource("Settings_Limit_Max")),
            new("Min", (string)FindResource("Settings_Limit_Min"))
        };
        if (prevLimit != null) CmbGoalLimitType.SelectedValue = prevLimit;
        else CmbGoalLimitType.SelectedIndex = 0;

        // Unit
        CmbGoalUnit.ItemsSource = new List<ComboBoxOption>
        {
            new("Minute", (string)FindResource("Settings_Unit_Minute")),
            new("Hour", (string)FindResource("Settings_Unit_Hour")),
            new("Day", (string)FindResource("Settings_Unit_Day"))
        };
        if (prevUnit != null) CmbGoalUnit.SelectedValue = prevUnit;
        else CmbGoalUnit.SelectedIndex = 1; // Default to Hour

        // Target Type
        CmbGoalTargetType.ItemsSource = new List<ComboBoxOption>
        {
            new("Category", (string)FindResource("Settings_Target_Category")),
            new("App", (string)FindResource("Settings_Target_App"))
        };
        if (prevTarget != null) CmbGoalTargetType.SelectedValue = prevTarget;
        else CmbGoalTargetType.SelectedIndex = 1; // Default to App
    }

    private async void SettingsPage_Loaded(object sender, RoutedEventArgs e)
    {
        var theme = await _themeService.GetThemeAsync();
        RbThemeDark.IsChecked = theme == "dark";
        RbThemeLight.IsChecked = theme != "dark";

        var lang = await _languageService.GetLanguageAsync();
        RbLanguageEn.IsChecked = lang == "en";
        RbLanguageTr.IsChecked = lang != "en";

        var startWithWindows = await _startupService.GetIsEnabledAsync();
        CbStartWithWindows.IsChecked = startWithWindows;

        var idleSeconds = _trackingService.IdleThresholdMs / 1000;
        TxtIdleThreshold.Text = idleSeconds.ToString();

        TxtMinSessionSeconds.Text = _trackingService.MinSessionSeconds.ToString();

        var recordWindowTitle = (await _repository.GetSettingAsync("record_window_title") ?? "true") == "true";
        CbRecordWindowTitle.IsChecked = recordWindowTitle;
        _trackingService.RecordWindowTitle = recordWindowTitle;

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

        // Odak modu
        var focusEnabled = (await _repository.GetSettingAsync("focus_mode_enabled") ?? "false") == "true";
        ChkFocusModeEnabled.IsChecked = focusEnabled;
        var focusBlockedStr = await _repository.GetSettingAsync("focus_mode_blocked_processes") ?? "";
        var focusBlockedSet = new HashSet<string>(focusBlockedStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        _focusModeBlockedItems.Clear();
        foreach (var app in _appExcludeItems)
            _focusModeBlockedItems.Add(new FocusModeBlockItem(app.ProcessName, app.DisplayName, focusBlockedSet.Contains(app.ProcessName)));

        // Uygulama kategorileri listesi
        var appsWithCategory = await _repository.GetTrackedAppsWithCategoryAsync();
        _categoryItems.Clear();
        foreach (var app in appsWithCategory)
            _categoryItems.Add(new CategoryItem(app.AppId, app.DisplayName ?? app.ProcessName, app.CategoryName ?? ""));
        EmptyCategoryMessage.Visibility = _categoryItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ShowCategorySearchPlaceholder();
        ApplyCategorySearchFilter();

        // Günlük rapor ayarları
        var reportEnabled = (await _repository.GetSettingAsync("daily_report_enabled") ?? "false") == "true";
        ChkDailyReportEnabled.IsChecked = reportEnabled;
        var reportTime = await _repository.GetSettingAsync("daily_report_time") ?? "21:00";
        var timeParts = reportTime.Split(':');
        if (timeParts.Length == 2)
        {
            CmbReportHour.SelectedItem = timeParts[0];
            CmbReportMinute.SelectedItem = timeParts[1];
        }

        // Akıllı öneri ayarları
        var insightsEnabled = (await _repository.GetSettingAsync("smart_insights_enabled") ?? "true") == "true";
        ChkSmartInsightsEnabled.IsChecked = insightsEnabled;

        await LoadGoalsAsync();
    }

    private void TxtDailyLimitSearch_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TxtDailyLimitSearch.Text == Strings.Get("Dashboard_Search"))
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
        TxtDailyLimitSearch.Text = Strings.Get("Dashboard_Search");
        TxtDailyLimitSearch.SetResourceReference(ForegroundProperty, "TextMutedBrush");
    }

    private void TxtDailyLimitSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyDailyLimitSearchFilter();
    }

    private void ApplyDailyLimitSearchFilter()
    {
        var raw = TxtDailyLimitSearch?.Text ?? "";
        var query = (raw == Strings.Get("Dashboard_Search") ? "" : raw).Trim();
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
        if (TxtAppSearch.Text == Strings.Get("Dashboard_Search"))
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
        TxtAppSearch.Text = Strings.Get("Dashboard_Search");
        TxtAppSearch.SetResourceReference(ForegroundProperty, "TextMutedBrush");
    }

    private void TxtAppSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyAppSearchFilter();
    }

    private void ApplyAppSearchFilter()
    {
        var raw = TxtAppSearch?.Text ?? "";
        var query = (raw == Strings.Get("Dashboard_Search") ? "" : raw).Trim();
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
        if (TxtCategorySearch.Text == Strings.Get("Dashboard_Search"))
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
        TxtCategorySearch.Text = Strings.Get("Dashboard_Search");
        TxtCategorySearch.SetResourceReference(ForegroundProperty, "TextMutedBrush");
    }

    private void TxtCategorySearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        ApplyCategorySearchFilter();
    }

    private void ApplyCategorySearchFilter()
    {
        var raw = TxtCategorySearch?.Text ?? "";
        var query = (raw == Strings.Get("Dashboard_Search") ? "" : raw).Trim();
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
        var selectedOption = CategoryComboBox.SelectedItem as ComboBoxOption;
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

    private void BtnAbout_Click(object sender, RoutedEventArgs e)
    {
        var frame = FindParent<Frame>(this);
        frame?.Navigate(new AboutPage());
    }

    private async void BtnDeleteOldData_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string tagStr || !int.TryParse(tagStr, out var days))
            return;
        var msg = string.Format(Strings.Get("Msg_ConfirmDeleteDays"), days);
        var result = System.Windows.MessageBox.Show(
            msg,
            Strings.Get("Msg_AppName"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            var cutoff = DateTime.Today.AddDays(-days);
            await _repository.DeleteDataOlderThanAsync(cutoff);
            System.Windows.MessageBox.Show(days + Strings.Get("Msg_DataDeleted"), Strings.Get("Msg_AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(Strings.Get("Msg_DeleteError") + ex.Message, Strings.Get("Msg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnDeleteAll_Click(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            Strings.Get("Msg_ConfirmDeleteAll"),
            Strings.Get("Msg_AppName"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;
        try
        {
            await _repository.DeleteAllDataAsync();
            System.Windows.MessageBox.Show(Strings.Get("Msg_AllDataDeleted"), Strings.Get("Msg_AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show(Strings.Get("Msg_DeleteError") + ex.Message, Strings.Get("Msg_Error"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void BtnSaveGeneral_Click(object sender, RoutedEventArgs e)
    {
        if (!uint.TryParse(TxtIdleThreshold.Text, out var seconds) || seconds < IdleMinSeconds || seconds > IdleMaxSeconds)
        {
            System.Windows.MessageBox.Show(Strings.Get("Msg_IdleRange"), Strings.Get("Msg_Error"), MessageBoxButton.OK, MessageBoxImage.Warning);
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

        var language = RbLanguageEn.IsChecked == true ? "en" : "tr";
        _languageService.ApplyLanguage(language);
        await _languageService.SaveLanguageAsync(language);

        await _startupService.SetEnabledAsync(CbStartWithWindows.IsChecked == true);

        var recordWindowTitle = CbRecordWindowTitle.IsChecked == true;
        await _repository.SetSettingAsync("record_window_title", recordWindowTitle ? "true" : "false");
        _trackingService.RecordWindowTitle = recordWindowTitle;
        
        // Refresh goal-related localized elements
        PopulateGoalComboBoxes();
        await PopulateGoalTargetsAsync();
        CollectionViewSource.GetDefaultView(_goals)?.Refresh();

        // Refresh search placeholders
        ShowAppSearchPlaceholder();
        ShowDailyLimitSearchPlaceholder();
        ShowCategorySearchPlaceholder();

        CategoryComboBox.ItemsSource = CategoryItem.GetCategoryOptions();
        _categoryView?.Refresh();
        System.Windows.MessageBox.Show(Strings.Get("Msg_GeneralSaved"), Strings.Get("Msg_AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnSaveAppExclude_Click(object sender, RoutedEventArgs e)
    {
        var ignored = string.Join(",", _appExcludeItems.Where(x => x.IsExcluded).Select(x => x.ProcessName));
        await _repository.SetSettingAsync("ignored_processes", ignored);
        var engine = _services.GetRequiredService<ITrackingEngine>();
        var userList = ignored.Length > 0 ? ignored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
        engine.SetIgnoredProcesses(["Monity.App", "explorer"], userList);
        System.Windows.MessageBox.Show(Strings.Get("Msg_ExcludeSaved"), Strings.Get("Msg_AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
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
        System.Windows.MessageBox.Show(Strings.Get("Msg_DailyLimitsSaved"), Strings.Get("Msg_AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnSaveCategory_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _categoryItems)
            await _repository.SetAppCategoryAsync(item.AppId, string.IsNullOrEmpty(item.CategoryName) ? null : item.CategoryName);
        System.Windows.MessageBox.Show(Strings.Get("Msg_CategoriesSaved"), Strings.Get("Msg_AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnSaveDailyReport_Click(object sender, RoutedEventArgs e)
    {
        var hour = CmbReportHour.SelectedItem as string ?? "21";
        var minute = CmbReportMinute.SelectedItem as string ?? "00";
        var timeStr = $"{hour}:{minute}";

        var enabled = ChkDailyReportEnabled.IsChecked == true;
        await _repository.SetSettingAsync("daily_report_enabled", enabled ? "true" : "false");
        await _repository.SetSettingAsync("daily_report_time", timeStr);
        
        System.Windows.MessageBox.Show(Strings.Get("Msg_GeneralSaved"), Strings.Get("Msg_AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnSaveSmartInsights_Click(object sender, RoutedEventArgs e)
    {
        var enabled = ChkSmartInsightsEnabled.IsChecked == true;
        await _repository.SetSettingAsync("smart_insights_enabled", enabled ? "true" : "false");
        
        System.Windows.MessageBox.Show(Strings.Get("Msg_GeneralSaved"), Strings.Get("Msg_AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void BtnSaveFocusMode_Click(object sender, RoutedEventArgs e)
    {
        var focusService = _services.GetRequiredService<FocusModeService>();
        var enabled = ChkFocusModeEnabled.IsChecked == true;
        await focusService.SetEnabledAsync(enabled);
        var blocked = _focusModeBlockedItems.Where(x => x.IsBlocked).Select(x => x.ProcessName).ToList();
        await focusService.SetBlockedProcessesAsync(blocked);
        System.Windows.MessageBox.Show(Strings.Get("Msg_GeneralSaved"), Strings.Get("Msg_AppName"), MessageBoxButton.OK, MessageBoxImage.Information);
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

    private sealed class FocusModeBlockItem : INotifyPropertyChanged
    {
        public string ProcessName { get; }
        public string DisplayName { get; }
        private bool _isBlocked;
        public bool IsBlocked
        {
            get => _isBlocked;
            set { _isBlocked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsBlocked))); }
        }
        public FocusModeBlockItem(string processName, string displayName, bool isBlocked)
        {
            ProcessName = processName;
            DisplayName = displayName;
            _isBlocked = isBlocked;
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
        public string LimitDisplayText => LimitMinutes is { } m && m > 0 ? $"{m} {Strings.Get("Settings_Unit_Minute")}" : "—";
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
        internal static IReadOnlyList<ComboBoxOption> GetCategoryOptions() =>
        [
            new ComboBoxOption("", Strings.Get("Category_Uncategorized")),
            new ComboBoxOption("Diğer", Strings.Get("Category_Other")),
            new ComboBoxOption("Tarayıcı", Strings.Get("Category_Browser")),
            new ComboBoxOption("Geliştirme", Strings.Get("Category_Development")),
            new ComboBoxOption("Sosyal", Strings.Get("Category_Social")),
            new ComboBoxOption("Eğlence", Strings.Get("Category_Entertainment")),
            new ComboBoxOption("Ofis", Strings.Get("Category_Office")),
            new ComboBoxOption("Eğitim", Strings.Get("Category_Education"))
        ];
        public int AppId { get; }
        public string DisplayName { get; }
        private string _categoryName;
        public string CategoryName
        {
            get => _categoryName;
            set { _categoryName = value ?? ""; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CategoryName))); }
        }
        public IReadOnlyList<ComboBoxOption> CategoryOptions => GetCategoryOptions();
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

    private sealed record ComboBoxOption(string Value, string Display);

    private async Task LoadGoalsAsync()
    {
        try
        {
            var goalProgresses = await _goalService.GetGoalProgressesAsync();
            _goals.Clear();
            foreach (var gp in goalProgresses)
                _goals.Add(gp.Goal);

            EmptyGoalsMessage.Visibility = _goals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error loading goals");
        }
    }

    private async void BtnAddGoal_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(TxtGoalValue.Text)) return;
            if (!int.TryParse(TxtGoalValue.Text, out int val)) return;
            
            var targetValue = CmbGoalTargetValue.SelectedItem?.ToString() ?? CmbGoalTargetValue.Text;
            if (string.IsNullOrWhiteSpace(targetValue)) return;

            var freqOption = CmbGoalFrequency.SelectedItem as ComboBoxOption;
            var freqStr = freqOption?.Value ?? "Daily";
            var freqDisplay = freqOption?.Display ?? "Daily";

            var limitOption = CmbGoalLimitType.SelectedItem as ComboBoxOption;
            var limitStr = limitOption?.Value ?? "Max";
            var limitDisplay = limitOption?.Display ?? "Max";

            var unitOption = CmbGoalUnit.SelectedItem as ComboBoxOption;
            var unitStr = unitOption?.Value ?? "Hour";
            var unitDisplay = unitOption?.Display ?? "Hour";

            var typeStr = CmbGoalTargetType.SelectedValue?.ToString() ?? "App";

            var frequency = Enum.Parse<GoalFrequency>(freqStr);
            var limitType = Enum.Parse<GoalLimitType>(limitStr);
            var targetType = Enum.Parse<GoalTargetType>(typeStr);

            int seconds = val;
            if (unitStr == "Minute") seconds *= 60;
            else if (unitStr == "Hour") seconds *= 3600;
            else if (unitStr == "Day") seconds *= 86400;

            // Localized title generation
            string title = $"{targetValue} ({freqDisplay} {limitDisplay} {val} {unitDisplay})";

            var id = await _goalService.AddGoalAsync(title, targetType, targetValue, limitType, seconds, frequency);
            if (id > 0)
            {
                TxtGoalValue.Text = "1";
                await LoadGoalsAsync();
                System.Windows.MessageBox.Show((string)System.Windows.Application.Current.FindResource("Msg_GoalAdded"), "Monity", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error adding goal");
        }
    }

    private void NumberValidationTextBox(object sender, TextCompositionEventArgs e)
    {
        Regex regex = new Regex("[^0-9]+");
        e.Handled = regex.IsMatch(e.Text);
    }

    private async void CmbGoalTargetType_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await PopulateGoalTargetsAsync();
    }

    private async Task PopulateGoalTargetsAsync()
    {
        if (CmbGoalTargetType == null || CmbGoalTargetValue == null) return;

        var targetType = CmbGoalTargetType.SelectedValue?.ToString();

        CmbGoalTargetValue.ItemsSource = null;

        if (targetType == "Category")
        {
            CmbGoalTargetValue.ItemsSource = CategoryItem.GetCategoryOptions().Select(o => o.Display).ToList();
        }
        else if (targetType == "App")
        {
            var apps = await _repository.GetTrackedAppsWithCategoryAsync();
            CmbGoalTargetValue.ItemsSource = apps.Select(a => a.DisplayName ?? a.ProcessName).Distinct().ToList();
        }
        
        if (CmbGoalTargetValue.Items.Count > 0)
            CmbGoalTargetValue.SelectedIndex = 0;
    }

    private async void BtnDeleteGoal_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.Tag is int goalId)
        {
            await _goalService.DeleteGoalAsync(goalId);
            await LoadGoalsAsync();
        }
    }
}

public class GoalFrequencyConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is GoalFrequency freq)
        {
            return freq switch
            {
                GoalFrequency.Daily => System.Windows.Application.Current.FindResource("Settings_Frequency_Daily"),
                GoalFrequency.Weekly => System.Windows.Application.Current.FindResource("Settings_Frequency_Weekly"),
                GoalFrequency.Monthly => System.Windows.Application.Current.FindResource("Settings_Frequency_Monthly"),
                _ => value.ToString()
            };
        }
        return value?.ToString() ?? "";
    }
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public class GoalLimitTypeConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is GoalLimitType limit)
        {
            return limit switch
            {
                GoalLimitType.Max => System.Windows.Application.Current.FindResource("Settings_Limit_Max"),
                GoalLimitType.Min => System.Windows.Application.Current.FindResource("Settings_Limit_Min"),
                _ => value.ToString()
            };
        }
        return value?.ToString() ?? "";
    }
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}

public class GoalTitleConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (values.Length >= 4)
        {
            string targetValue = values[0]?.ToString() ?? "";
            GoalFrequency freq = (values[1] is GoalFrequency f) ? f : GoalFrequency.Daily;
            GoalLimitType limit = (values[2] is GoalLimitType l) ? l : GoalLimitType.Max;
            
            int seconds = 0;
            if (values[3] != null)
            {
                try { seconds = System.Convert.ToInt32(values[3]); }
                catch { }
            }

            string freqStr = (string)System.Windows.Application.Current.FindResource($"Settings_Frequency_{freq}");
            string limitStr = (string)System.Windows.Application.Current.FindResource($"Settings_Limit_{limit}");
            
            string unitStr;
            int val;
            if (seconds >= 86400 && seconds % 86400 == 0) 
            { 
                val = seconds / 86400; 
                unitStr = (string)System.Windows.Application.Current.FindResource("Settings_Unit_Day"); 
            }
            else if (seconds >= 3600 && seconds % 3600 == 0) 
            { 
                val = seconds / 3600; 
                unitStr = (string)System.Windows.Application.Current.FindResource("Settings_Unit_Hour"); 
            }
            else 
            { 
                val = seconds / 60; 
                unitStr = (string)System.Windows.Application.Current.FindResource("Settings_Unit_Minute"); 
            }

            return $"{targetValue} ({freqStr} {limitStr} {val} {unitStr})";
        }
        return "";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
}
