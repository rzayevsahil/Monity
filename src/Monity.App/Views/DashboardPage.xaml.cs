using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
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

    public DashboardPage(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _repository = services.GetRequiredService<IUsageRepository>();
        _trackingService = services.GetRequiredService<UsageTrackingService>();

        DatePicker.SelectedDate = DateTime.Today;
        AppListView.ItemsSource = _appItems;
        SetupHourlyChartAxes();

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

    private async void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        await LoadDataAsync(GetSelectedDateString());
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        BtnRefresh.IsEnabled = false;
        try
        {
            await LoadDataAsync(GetSelectedDateString());
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
        }
    }

    private string GetSelectedDateString()
    {
        if (DatePicker.SelectedDate.HasValue)
            return DatePicker.SelectedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
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
        return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private async System.Threading.Tasks.Task LoadDataAsync(string date)
    {
        try
        {
            var (total, sessionCount) = await _repository.GetDailyTotalAsync(date, excludeIdle: true);
            var apps = await _repository.GetDailyUsageAsync(date, excludeIdle: true);
            var hourly = await _repository.GetHourlyUsageAsync(date, excludeIdle: true);

            await Dispatcher.InvokeAsync(() =>
            {
                ApplyDataToUI(total, sessionCount, apps, hourly);
            }, System.Windows.Threading.DispatcherPriority.Normal);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load dashboard data");
            await Dispatcher.InvokeAsync(() =>
            {
                TxtTodayTotal.Text = "?";
                TxtSessionCount.Text = "?";
            });
        }
    }

    private void ApplyDataToUI(long total, int sessionCount,
        System.Collections.Generic.IReadOnlyList<AppUsageSummary> apps,
        System.Collections.Generic.IReadOnlyList<HourlyUsage> hourly)
    {
        _totalSeconds = total;
        TxtTodayTotal.Text = FormatDuration(total);
        TxtSessionCount.Text = sessionCount.ToString();

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
        HourlyChart.Series = new ObservableCollection<ISeries>
        {
            new ColumnSeries<double>
            {
                Values = hourlyValues,
                Fill = new SolidColorPaint(new SKColor(37, 99, 235))
            }
        };
    }

    private void SetupHourlyChartAxes()
    {
        var textColor = new SKColor(100, 116, 139); // slate
        var separatorColor = new SKColor(226, 232, 240);

        HourlyChart.XAxes = new List<Axis>
        {
            new Axis
            {
                Labeler = value =>
                {
                    var h = (int)value;
                    return h >= 0 && h < 24 && (h % 3 == 0) ? h.ToString() : string.Empty;
                },
                LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = SKTypeface.FromFamilyName("Segoe UI") },
                SeparatorsPaint = new SolidColorPaint(separatorColor) { StrokeThickness = 1 },
                MinStep = 1
            }
        };
        HourlyChart.YAxes = new List<Axis>
        {
            new Axis
            {
                Labeler = value => $"{(int)value} dk",
                LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = SKTypeface.FromFamilyName("Segoe UI") },
                SeparatorsPaint = new SolidColorPaint(separatorColor) { StrokeThickness = 1 },
                MinStep = 15
            }
        };
    }

    private static string FormatDuration(long seconds)
    {
        var h = seconds / 3600;
        var m = (seconds % 3600) / 60;
        if (h > 0)
            return $"{h}h {m}m";
        if (m > 0)
            return $"{m}m";
        return $"{seconds}s";
    }

    private class AppUsageItem
    {
        public string DisplayName { get; set; } = "";
        public string DurationFormatted { get; set; } = "";
        public string PercentageFormatted { get; set; } = "";
    }
}
