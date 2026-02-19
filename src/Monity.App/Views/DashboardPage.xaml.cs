using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
using Monity.Infrastructure;
using Monity.App.Helpers;
using Monity.Infrastructure.Persistence;
using Monity.Infrastructure.Tracking;
using SkiaSharp;

namespace Monity.App.Views;

public partial class DashboardPage : Page
{
    private readonly IServiceProvider _services;
    private readonly IUsageRepository _repository;
    private readonly UsageTrackingService _trackingService;
    private long _totalSeconds;
    private ObservableCollection<AppUsageItem> _appItems = [];
    private readonly ObservableCollection<HeatMapDayCell> _heatMapCells = [];
    private ICollectionView _appListView = null!;

    public DashboardPage(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _repository = services.GetRequiredService<IUsageRepository>();
        _trackingService = services.GetRequiredService<UsageTrackingService>();

        DatePicker.SelectedDate = DateTime.Today;
        _appListView = CollectionViewSource.GetDefaultView(_appItems);
        AppListView.ItemsSource = _appListView;
        HeatMapItemsControl.ItemsSource = _heatMapCells;
        SetupHourlyChartAxes();
        SetupCategoryFilter();

        Loaded += async (_, _) =>
        {
            _trackingService.ForegroundChanged += OnForegroundChanged;
            await LoadDataAsync(DateTime.Today.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        };

        Unloaded += (_, _) => _trackingService.ForegroundChanged -= OnForegroundChanged;
    }

    private void OnForegroundChanged(object? sender, ForegroundChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            var displayName = AppDisplayNameResolver.GetDisplayNameFromExe(e.ExePath);
            TxtCurrentApp.Text = !string.IsNullOrEmpty(displayName) ? displayName : e.ProcessName;
        });
    }

    private void SetupCategoryFilter()
    {
        var options = new List<CategoryFilterOption>
        {
            new(null, Strings.Get("Category_All")),
            new("", Strings.Get("Category_Uncategorized")),
            new("Diğer", Strings.Get("Category_Other")),
            new("Tarayıcı", Strings.Get("Category_Browser")),
            new("Geliştirme", Strings.Get("Category_Development")),
            new("Sosyal", Strings.Get("Category_Social")),
            new("Eğlence", Strings.Get("Category_Entertainment")),
            new("Ofis", Strings.Get("Category_Office"))
        };
        CategoryFilter.ItemsSource = options;
        CategoryFilter.DisplayMemberPath = "Display";
        CategoryFilter.SelectedValuePath = "Value";
        CategoryFilter.SelectedIndex = 0;
    }

    private string? GetSelectedCategoryName()
    {
        if (CategoryFilter.SelectedItem is CategoryFilterOption opt)
            return opt.Value;
        return null;
    }

    private record CategoryFilterOption(string? Value, string Display);

    private async void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        await LoadDataAsync(GetSelectedDateString());
    }

    private async void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        await LoadDataAsync(GetSelectedDateString());
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        BtnRefresh.IsEnabled = false;
        try
        {
            // DatePicker'dan yeni seçilen tarihin commit edilmesi için odağı butona al (özellikle yazılan tarih için)
            BtnRefresh.Focus();
            await LoadDataAsync(GetSelectedDateString());
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
        }
    }

    private string GetSelectedDateString()
    {
        // Önce kutuda görünen metni kullan (kullanıcının gördüğü tarih = takvim veya yazılan)
        var text = DatePicker.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var cultures = new[] { CultureInfo.CurrentCulture, new CultureInfo("tr-TR"), CultureInfo.InvariantCulture };
            foreach (var culture in cultures)
            {
                if (DateTime.TryParse(text, culture, DateTimeStyles.None, out var parsed))
                    return parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }
        if (DatePicker.SelectedDate.HasValue)
            return DatePicker.SelectedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private async System.Threading.Tasks.Task LoadDataAsync(string date)
    {
        if (string.IsNullOrWhiteSpace(date))
            return;
        try
        {
            // Buffer'daki bekleyen oturumları önce DB'ye yaz; aksi halde henüz flush olmamış veri okunmaz.
            await _trackingService.FlushBufferAsync();
            await _repository.UpdateDailySummaryAsync(date);

            var ignoredStr = await _repository.GetSettingAsync("ignored_processes") ?? "";
            var excluded = ignoredStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            var categoryName = GetSelectedCategoryName();
            var (total, sessionCount) = await _repository.GetDailyTotalAsync(date, excludeIdle: true, excludedProcessNames: excluded, categoryName: categoryName);
            var apps = await _repository.GetDailyUsageAsync(date, excludeIdle: true, excludedProcessNames: excluded, categoryName: categoryName);
            var hourly = await _repository.GetHourlyUsageAsync(date, excludeIdle: true, categoryName: categoryName);
            var firstActivity = await _repository.GetFirstSessionStartedAtAsync(date);

            var selectedDate = DateTime.ParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var startOfMonth = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
            var excludedList = excluded.ToList();
            var dailyTotalsForMonth = await _repository.GetDailyTotalsInRangeAsync(startOfMonth, endOfMonth, excludeIdle: true, excludedProcessNames: excludedList, categoryName: categoryName);

            var language = (await _repository.GetSettingAsync("language") ?? "tr").Trim().ToLowerInvariant();
            var titleCulture = language == "en" ? new CultureInfo("en-US") : new CultureInfo("tr-TR");
            await Dispatcher.InvokeAsync(() =>
            {
                ApplyDataToUI(total, sessionCount, apps, hourly, firstActivity);
                UpdateHeatMap(dailyTotalsForMonth, startOfMonth, endOfMonth, selectedDate);
                TxtHeatMapTitle.Text = $"{Strings.Get("Dashboard_HeatMapTitle")} — {startOfMonth.ToString("MMMM yyyy", titleCulture)}";
            }, System.Windows.Threading.DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load dashboard data");
            await Dispatcher.InvokeAsync(() => ClearDashboardData());
        }
    }

    /// <summary>
    /// Liste ve grafiği boşaltır; veri yok veya hata durumunda kullanılır.
    /// </summary>
    private void ClearDashboardData()
    {
        TxtTodayTotal.Text = "0 sa 0 dk";
        TxtSessionCount.Text = "0";
        TxtFirstActivity.Text = Strings.Get("Dashboard_FirstActivity");
        TxtHeatMapTitle.Text = Strings.Get("Dashboard_HeatMapTitle");
        _appItems.Clear();
        _heatMapCells.Clear();
        HourlyChart.Series = new ObservableCollection<ISeries>();
    }

    private void AppSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = (AppSearchBox.Text ?? "").Trim();
        AppSearchPlaceholder.Visibility = string.IsNullOrEmpty(AppSearchBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
        _appListView.Filter = string.IsNullOrEmpty(query)
            ? null
            : obj => obj is AppUsageItem item &&
                     item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
        _appListView.Refresh();
    }

    private void ApplyDataToUI(long total, int sessionCount,
        System.Collections.Generic.IReadOnlyList<AppUsageSummary> apps,
        System.Collections.Generic.IReadOnlyList<HourlyUsage> hourly,
        DateTime? firstActivity = null)
    {
        _totalSeconds = total;
        TxtTodayTotal.Text = FormatDuration(total);
        TxtSessionCount.Text = sessionCount.ToString();
        TxtFirstActivity.Text = firstActivity.HasValue
            ? Strings.Get("Dashboard_FirstActivityPrefix") + firstActivity.Value.ToString("HH:mm")
            : Strings.Get("Dashboard_FirstActivity");

        _appItems.Clear();
        foreach (var a in apps)
        {
            var pct = _totalSeconds > 0 ? (double)a.TotalSeconds / _totalSeconds * 100 : 0;
            _appItems.Add(new AppUsageItem
            {
                DisplayName = a.DisplayName ?? a.ProcessName,
                DurationFormatted = FormatDuration(a.TotalSeconds),
                PercentageFormatted = $"{pct:F1}%"
            });
        }

        var hourlyValues = new double[24];
        foreach (var h in hourly)
            hourlyValues[(int)h.Hour] = h.TotalSeconds / 60.0; // Dakika
        HourlyChart.Series = new ObservableCollection<ISeries>();
        HourlyChart.Series = new ObservableCollection<ISeries>
        {
            new ColumnSeries<double>
            {
                Values = hourlyValues,
                Fill = new SolidColorPaint(new SKColor(37, 99, 235)),
                Stroke = new SolidColorPaint(new SKColor(29, 78, 216)) { StrokeThickness = 1 },
                Rx = 4,
                Ry = 4
            }
        };
    }

    private void SetupHourlyChartAxes()
    {
        var textColor = new SKColor(71, 85, 105);
        var separatorColor = new SKColor(226, 232, 240);
        var font = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        HourlyChart.XAxes = new List<Axis>
        {
            new Axis
            {
                Labeler = value =>
                {
                    var h = (int)Math.Round(value);
                    return h >= 0 && h < 24 ? h.ToString() : string.Empty;
                },
                LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = font },
                SeparatorsPaint = new SolidColorPaint(separatorColor) { StrokeThickness = 1 },
                MinStep = 1,
                NamePaint = null
            }
        };
        HourlyChart.YAxes = new List<Axis>
        {
            new Axis
            {
                Labeler = value =>
                {
                    var mins = (int)value;
                    if (mins >= 60) return $"{mins / 60} sa";
                    return mins > 0 ? $"{mins} dk" : "0 dk";
                },
                LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = font },
                SeparatorsPaint = new SolidColorPaint(separatorColor) { StrokeThickness = 1 },
                MinStep = 15,
                NamePaint = null
            }
        };
    }

    private static string FormatDuration(long seconds)
    {
        var h = seconds / 3600;
        var m = (seconds % 3600) / 60;
        if (h > 0)
            return $"{h} sa {m} dk";
        if (m > 0)
            return $"{m} dk";
        return $"{seconds} sn";
    }

    private static readonly System.Windows.Media.Brush HeatMapLabelDark =
        (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#1E293B")!;
    private static readonly System.Windows.Media.Brush HeatMapLabelLight =
        (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#F8FAFC")!;

    private static bool IsDarkTheme()
    {
        var app = System.Windows.Application.Current?.Resources;
        if (app?.MergedDictionaries.Count > 0 && app.MergedDictionaries[0].Source != null)
            return app.MergedDictionaries[0].Source.ToString().IndexOf("Dark.xaml", StringComparison.OrdinalIgnoreCase) >= 0;
        return false;
    }

    private static System.Windows.Media.Brush[] GetHeatMapBrushes()
    {
        var card = System.Windows.Application.Current.Resources["CardBrush"] as System.Windows.Media.Brush;
        var primary = System.Windows.Application.Current.Resources["PrimaryBrush"] as System.Windows.Media.SolidColorBrush;
        var color = primary?.Color ?? System.Windows.Media.Color.FromRgb(59, 130, 246);
        var fallbackCard = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFrom("#EBEDF0")!;
        return
        [
            card ?? fallbackCard,
            new System.Windows.Media.SolidColorBrush(color) { Opacity = 0.25 },
            new System.Windows.Media.SolidColorBrush(color) { Opacity = 0.5 },
            new System.Windows.Media.SolidColorBrush(color) { Opacity = 0.75 },
            primary ?? new System.Windows.Media.SolidColorBrush(color)
        ];
    }

    private void UpdateHeatMap(IReadOnlyList<DailyTotalByDate> dailyTotals, DateTime startOfMonth, DateTime endOfMonth, DateTime selectedDate)
    {
        var brushes = GetHeatMapBrushes();
        var dict = dailyTotals.ToDictionary(x => x.Date, x => x.TotalSeconds);
        var daysInMonth = endOfMonth.Day;
        var firstDay = startOfMonth.DayOfWeek;
        var startOffset = firstDay == DayOfWeek.Sunday ? 6 : (int)firstDay - 1;
        var maxSeconds = dict.Values.DefaultIfEmpty(0).Max();

        _heatMapCells.Clear();
        for (var i = 0; i < 42; i++)
        {
            if (i < startOffset || i >= startOffset + daysInMonth)
            {
                _heatMapCells.Add(new HeatMapDayCell(brushes[0], "", "", System.Windows.Media.Brushes.Transparent));
                continue;
            }
            var dayNum = i - startOffset + 1;
            var dateStr = $"{startOfMonth.Year}-{startOfMonth.Month:D2}-{dayNum:D2}";
            var secs = dict.GetValueOrDefault(dateStr, 0L);
            var level = maxSeconds > 0 ? (int)Math.Min(3, (double)secs / maxSeconds * 3.99) : 0;
            var fill = brushes[level + 1];
            var dayDate = new DateTime(startOfMonth.Year, startOfMonth.Month, dayNum);
            var tooltip = $"{dayDate:dd MMMM} · {FormatDuration(secs)}";
            var labelForeground = IsDarkTheme()
                ? HeatMapLabelLight
                : (level <= 1 ? HeatMapLabelDark : HeatMapLabelLight);
            _heatMapCells.Add(new HeatMapDayCell(fill, tooltip, dayNum.ToString(), labelForeground));
        }
    }

    private sealed class HeatMapDayCell
    {
        public System.Windows.Media.Brush Fill { get; }
        public string TooltipText { get; }
        public string DayLabel { get; }
        public System.Windows.Media.Brush DayLabelForeground { get; }

        public HeatMapDayCell(System.Windows.Media.Brush fill, string tooltipText, string dayLabel, System.Windows.Media.Brush dayLabelForeground)
        {
            Fill = fill;
            TooltipText = tooltipText;
            DayLabel = dayLabel;
            DayLabelForeground = dayLabelForeground;
        }
    }

    private class AppUsageItem
    {
        public string DisplayName { get; set; } = "";
        public string DurationFormatted { get; set; } = "";
        public string PercentageFormatted { get; set; } = "";
    }
}
