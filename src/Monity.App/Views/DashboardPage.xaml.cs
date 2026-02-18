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
            new(null, "Tümü"),
            new("", "Kategorisiz"),
            new("Diğer", "Diğer"),
            new("Tarayıcı", "Tarayıcı"),
            new("Geliştirme", "Geliştirme"),
            new("Sosyal", "Sosyal"),
            new("Eğlence", "Eğlence"),
            new("Ofis", "Ofis")
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

            await Dispatcher.InvokeAsync(() =>
            {
                ApplyDataToUI(total, sessionCount, apps, hourly, firstActivity);
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
        TxtFirstActivity.Text = "Bugün başlangıç: —";
        _appItems.Clear();
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
            ? $"Bugün başlangıç: {firstActivity.Value:HH:mm}"
            : "Bugün başlangıç: —";

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

    private class AppUsageItem
    {
        public string DisplayName { get; set; } = "";
        public string DurationFormatted { get; set; } = "";
        public string PercentageFormatted { get; set; } = "";
    }
}
