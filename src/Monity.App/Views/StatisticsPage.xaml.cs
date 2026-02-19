using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Microsoft.Extensions.DependencyInjection;
using Monity.App.Helpers;
using Monity.Infrastructure.Persistence;
using SkiaSharp;

namespace Monity.App.Views;

public partial class StatisticsPage : Page
{
    private readonly IServiceProvider _services;
    private readonly IUsageRepository _repository;
    private ObservableCollection<StatAppItem> _appItems = [];
    private ICollectionView _appListView = null!;

    public StatisticsPage(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _repository = services.GetRequiredService<IUsageRepository>();

        DatePicker.SelectedDate = DateTime.Today;
        RbDaily.IsChecked = true;

        _appListView = CollectionViewSource.GetDefaultView(_appItems);
        AppListView.ItemsSource = _appListView;
        SetupCategoryFilter();

        Loaded += async (_, _) => await LoadDataAsync();
        SetupTimeChartAxes();
    }

    private DurationAndPeriodHelper.PeriodKind GetPeriod()
    {
        if (RbDaily?.IsChecked == true) return DurationAndPeriodHelper.PeriodKind.Daily;
        if (RbWeekly?.IsChecked == true) return DurationAndPeriodHelper.PeriodKind.Weekly;
        if (RbMonthly?.IsChecked == true) return DurationAndPeriodHelper.PeriodKind.Monthly;
        if (RbYearly?.IsChecked == true) return DurationAndPeriodHelper.PeriodKind.Yearly;
        return DurationAndPeriodHelper.PeriodKind.Daily;
    }

    private DateTime GetSelectedDate()
    {
        if (DatePicker.SelectedDate.HasValue)
            return DatePicker.SelectedDate.Value;
        var text = DatePicker.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var cultures = new[] { CultureInfo.CurrentCulture, new CultureInfo("tr-TR"), CultureInfo.InvariantCulture };
            foreach (var culture in cultures)
            {
                if (DateTime.TryParse(text, culture, DateTimeStyles.None, out var parsed))
                    return parsed;
            }
        }
        return DateTime.Today;
    }

    private DateTime? GetSelectedEndDate()
    {
        if (DatePickerEnd.SelectedDate.HasValue)
            return DatePickerEnd.SelectedDate.Value;
        var text = DatePickerEnd.Text;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var cultures = new[] { CultureInfo.CurrentCulture, new CultureInfo("tr-TR"), CultureInfo.InvariantCulture };
            foreach (var culture in cultures)
            {
                if (DateTime.TryParse(text, culture, DateTimeStyles.None, out var parsed))
                    return parsed;
            }
        }
        return null;
    }

    private void SetupCategoryFilter()
    {
        var options = new List<StatCategoryFilterOption>
        {
            new(null, Strings.Get("Category_All")),
            new("", Strings.Get("Category_Uncategorized")),
            new("Diğer", Strings.Get("Category_Other")),
            new("Tarayıcı", Strings.Get("Category_Browser")),
            new("Geliştirme", Strings.Get("Category_Development")),
            new("Sosyal", Strings.Get("Category_Social")),
            new("Eğlence", Strings.Get("Category_Entertainment")),
            new("Ofis", Strings.Get("Category_Office")),
            new("Eğitim", Strings.Get("Category_Education"))
        };
        CategoryFilter.ItemsSource = options;
        CategoryFilter.DisplayMemberPath = "Display";
        CategoryFilter.SelectedValuePath = "Value";
        CategoryFilter.SelectedIndex = 0;
    }

    private string? GetSelectedCategoryName()
    {
        return CategoryFilter.SelectedValue as string;
    }

    private sealed record StatCategoryFilterOption(string? Value, string Display);

    private void Period_Changed(object sender, RoutedEventArgs e) => _ = LoadDataAsync();

    private async void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => await LoadDataAsync();

    private async void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e) => await LoadDataAsync();

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        BtnRefresh.IsEnabled = false;
        try
        {
            BtnRefresh.Focus();
            await LoadDataAsync();
        }
        finally
        {
            BtnRefresh.IsEnabled = true;
        }
    }

    private void AppSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = (AppSearchBox.Text ?? "").Trim();
        AppSearchPlaceholder.Visibility = string.IsNullOrEmpty(AppSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _appListView.Filter = string.IsNullOrEmpty(query)
            ? null
            : obj => obj is StatAppItem item && item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
        _appListView.Refresh();
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        var frame = FindParent<Frame>(this);
        frame?.Navigate(new DashboardPage(_services));
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

    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        var period = GetPeriod();
        var dateStart = GetSelectedDate();
        var dateEnd = GetSelectedEndDate();

        DateTime start;
        DateTime end;
        int dayCount;
        bool useHourly;

        if (dateEnd.HasValue && dateEnd.Value.Date >= dateStart.Date)
        {
            start = dateStart.Date;
            end = dateEnd.Value.Date;
            dayCount = (end - start).Days + 1;
            useHourly = start == end;
        }
        else
        {
            (start, end, dayCount) = DurationAndPeriodHelper.GetPeriodRange(period, dateStart);
            useHourly = period == DurationAndPeriodHelper.PeriodKind.Daily;
        }

        try
        {
            var ignoredStr = await _repository.GetSettingAsync("ignored_processes") ?? "";
            var excluded = ignoredStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

            var categoryName = GetSelectedCategoryName();
            var totalTask = _repository.GetRangeTotalAsync(start, end, excludeIdle: true, excludedProcessNames: excluded, categoryName: categoryName);
            var appsTask = _repository.GetWeeklyUsageAsync(start, end, excludeIdle: true, excludedProcessNames: excluded, categoryName: categoryName);

            var (startThisWeek, endThisWeek, _) = DurationAndPeriodHelper.GetPeriodRange(DurationAndPeriodHelper.PeriodKind.Weekly, DateTime.Today);
            var startLastWeek = startThisWeek.AddDays(-7);
            var endLastWeek = endThisWeek.AddDays(-7);
            var totalThisWeekTask = _repository.GetRangeTotalAsync(startThisWeek, endThisWeek, excludeIdle: true, excludedProcessNames: excluded, categoryName: categoryName);
            var totalLastWeekTask = _repository.GetRangeTotalAsync(startLastWeek, endLastWeek, excludeIdle: true, excludedProcessNames: excluded, categoryName: categoryName);

            System.Threading.Tasks.Task<IReadOnlyList<HourlyUsage>>? hourlyTask = null;
            System.Threading.Tasks.Task<IReadOnlyList<DailyTotalByDate>>? dailyTotalsTask = null;

            if (useHourly)
                hourlyTask = _repository.GetHourlyUsageAsync(start.ToString("yyyy-MM-dd"), excludeIdle: true, categoryName: categoryName);
            else
                dailyTotalsTask = _repository.GetDailyTotalsInRangeAsync(start, end, excludeIdle: true, excludedProcessNames: excluded, categoryName: categoryName);

            await System.Threading.Tasks.Task.WhenAll(
                new System.Threading.Tasks.Task[] { totalTask, appsTask, totalThisWeekTask, totalLastWeekTask }
                    .Concat(hourlyTask != null ? [hourlyTask] : dailyTotalsTask != null ? [dailyTotalsTask] : []));

            var total = await totalTask;
            var apps = await appsTask;
            var totalThisWeek = await totalThisWeekTask;
            var totalLastWeek = await totalLastWeekTask;
            IReadOnlyList<HourlyUsage>? hourly = hourlyTask != null ? await hourlyTask : null;
            IReadOnlyList<DailyTotalByDate>? dailyTotals = dailyTotalsTask != null ? await dailyTotalsTask : null;

            await Dispatcher.InvokeAsync(() =>
            {
                ApplyData(total.TotalSeconds, total.SessionCount, apps, dayCount, hourly, dailyTotals, period);
                ApplyWeeklyComparison(totalThisWeek.TotalSeconds, totalLastWeek.TotalSeconds);
                if (start.Date == end.Date && period == DurationAndPeriodHelper.PeriodKind.Daily && !dateEnd.HasValue)
                    TxtDateRange.Text = "";
                else
                    TxtDateRange.Text = $"{start.ToString("d", CultureInfo.CurrentCulture)} – {end.ToString("d", CultureInfo.CurrentCulture)}";
            });
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load statistics");
            await Dispatcher.InvokeAsync(ClearData);
        }
    }

    private void ApplyData(long totalSeconds, int sessionCount, System.Collections.Generic.IReadOnlyList<AppUsageSummary> apps, int dayCount,
        IReadOnlyList<HourlyUsage>? hourly, IReadOnlyList<DailyTotalByDate>? dailyTotals, DurationAndPeriodHelper.PeriodKind period)
    {
        TxtTotal.Text = DurationAndPeriodHelper.FormatDuration(totalSeconds);
        TxtSessionCount.Text = sessionCount.ToString();
        var avgSeconds = dayCount > 0 ? totalSeconds / dayCount : 0L;
        TxtAverage.Text = DurationAndPeriodHelper.FormatDuration(avgSeconds);

        _appItems.Clear();
        foreach (var a in apps)
        {
            var appAvg = dayCount > 0 ? a.TotalSeconds / dayCount : 0L;
            var pct = totalSeconds > 0 ? (double)a.TotalSeconds / totalSeconds * 100 : 0;
            _appItems.Add(new StatAppItem
            {
                DisplayName = a.DisplayName ?? a.ProcessName,
                TotalFormatted = DurationAndPeriodHelper.FormatDuration(a.TotalSeconds),
                AverageFormatted = DurationAndPeriodHelper.FormatDuration(appAvg),
                PercentageFormatted = $"{pct:F1}%"
            });
        }

        var primaryColor = new SKColor(37, 99, 235);
        var strokeColor = new SKColor(29, 78, 216);
        var font = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        var textColor = new SKColor(71, 85, 105);
        var separatorColor = new SKColor(226, 232, 240);

        if (hourly != null)
        {
            TxtTimeChartTitle.Text = Strings.Get("Stats_HourlyUsage");
            var hourlyValues = new double[24];
            foreach (var h in hourly)
                hourlyValues[(int)h.Hour] = h.TotalSeconds / 60.0;
            TimeDistributionChart.Series = new ObservableCollection<ISeries>
            {
                new ColumnSeries<double>
                {
                    Values = hourlyValues,
                    Fill = new SolidColorPaint(primaryColor),
                    Stroke = new SolidColorPaint(strokeColor) { StrokeThickness = 1 },
                    Rx = 4,
                    Ry = 4
                }
            };
            TimeDistributionChart.XAxes = new List<Axis>
            {
                new Axis
                {
                    Labeler = v => { var h = (int)Math.Round(v); return h >= 0 && h < 24 ? h.ToString() : ""; },
                    LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = font },
                    SeparatorsPaint = new SolidColorPaint(separatorColor) { StrokeThickness = 1 },
                    MinStep = 1,
                    NamePaint = null
                }
            };
        }
        else if (dailyTotals != null)
        {
            TxtTimeChartTitle.Text = Strings.Get("Stats_DailyTotals");
            var labels = dailyTotals.Select(d => d.Date.Length >= 10 ? d.Date[8..10] + "/" + d.Date[5..7] : d.Date).ToArray();
            var values = dailyTotals.Select(d => d.TotalSeconds / 60.0).ToArray();
            TimeDistributionChart.Series = new ObservableCollection<ISeries>
            {
                new ColumnSeries<double>
                {
                    Values = values,
                    Fill = new SolidColorPaint(primaryColor),
                    Stroke = new SolidColorPaint(strokeColor) { StrokeThickness = 1 },
                    Rx = 4,
                    Ry = 4
                }
            };
            TimeDistributionChart.XAxes = new List<Axis>
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = font },
                    SeparatorsPaint = new SolidColorPaint(separatorColor) { StrokeThickness = 1 },
                    NamePaint = null
                }
            };
        }
        else
        {
            TxtTimeChartTitle.Text = Strings.Get("Stats_HourlyUsage");
            TimeDistributionChart.Series = new ObservableCollection<ISeries>();
        }

        TimeDistributionChart.YAxes = new List<Axis>
        {
            new Axis
            {
                Labeler = v => { var m = (int)v; if (m >= 60) return $"{m / 60} sa"; return m > 0 ? $"{m} dk" : "0 dk"; },
                LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = font },
                SeparatorsPaint = new SolidColorPaint(separatorColor) { StrokeThickness = 1 },
                MinStep = 15,
                NamePaint = null
            }
        };

        var pieColors = new[] {
            new SKColor(37, 99, 235), new SKColor(59, 130, 246), new SKColor(96, 165, 250),
            new SKColor(147, 51, 234), new SKColor(168, 85, 247), new SKColor(192, 132, 252),
            new SKColor(34, 197, 94), new SKColor(74, 222, 128), new SKColor(234, 88, 12), new SKColor(251, 146, 60)
        };
        var pieSeries = new List<ISeries>();
        var topApps = apps.Take(10).ToList();
        for (var i = 0; i < topApps.Count; i++)
        {
            var a = topApps[i];
            var val = totalSeconds > 0 ? (double)a.TotalSeconds / 60.0 : 0;
            if (val <= 0) continue;
            pieSeries.Add(new PieSeries<double>
            {
                Values = new[] { val },
                Name = (a.DisplayName ?? a.ProcessName).Length > 20 ? (a.DisplayName ?? a.ProcessName)[..17] + "..." : (a.DisplayName ?? a.ProcessName),
                Fill = new SolidColorPaint(pieColors[i % pieColors.Length]),
                DataLabelsPaint = new SolidColorPaint(textColor) { SKTypeface = font },
                DataLabelsSize = 11,
                DataLabelsFormatter = point => ((double)point.Coordinate.PrimaryValue).ToString("F2", CultureInfo.CurrentCulture) + " dk",
                ToolTipLabelFormatter = point => ((double)point.Coordinate.PrimaryValue).ToString("F2", CultureInfo.CurrentCulture) + " dk"
            });
        }
        var restSeconds = totalSeconds > 0 ? apps.Skip(10).Sum(x => x.TotalSeconds) : 0L;
        if (restSeconds > 0)
        {
            pieSeries.Add(new PieSeries<double>
            {
                Values = new[] { restSeconds / 60.0 },
                Name = "Diğer",
                Fill = new SolidColorPaint(new SKColor(148, 163, 184)),
                DataLabelsPaint = new SolidColorPaint(textColor) { SKTypeface = font },
                DataLabelsSize = 11,
                DataLabelsFormatter = point => ((double)point.Coordinate.PrimaryValue).ToString("F2", CultureInfo.CurrentCulture) + " dk",
                ToolTipLabelFormatter = point => ((double)point.Coordinate.PrimaryValue).ToString("F2", CultureInfo.CurrentCulture) + " dk"
            });
        }
        AppDistributionChart.Series = new ObservableCollection<ISeries>(pieSeries);
    }

    private void ApplyWeeklyComparison(long thisWeekSeconds, long lastWeekSeconds)
    {
        TxtWeeklyThisWeek.Text = Strings.Get("Stats_ThisWeekLabel")+ " " + DurationAndPeriodHelper.FormatDuration(thisWeekSeconds);
        TxtWeeklyLastWeek.Text = Strings.Get("Stats_LastWeekLabel") + " " + DurationAndPeriodHelper.FormatDuration(lastWeekSeconds);
        var diffSeconds = thisWeekSeconds - lastWeekSeconds;
        var diffFormatted = DurationAndPeriodHelper.FormatDuration(Math.Abs(diffSeconds));
        var pct = lastWeekSeconds > 0 ? (double)diffSeconds / lastWeekSeconds * 100 : (diffSeconds > 0 ? 100.0 : 0.0);
        var pctStr = pct >= 0 ? $"%+{pct:F0}" : $"%{pct:F0}";
        var diffPrefix = Strings.Get("Stats_DiffLabel");
        TxtWeeklyDiff.Text = diffSeconds >= 0
            ? diffPrefix + " " + "+" + diffFormatted + " (" + pctStr + ")"
            : diffPrefix + " " + "-" + diffFormatted + " (" + pctStr + ")";
    }

    private void ClearData()
    {
        TxtTotal.Text = "0 sa 0 dk";
        TxtAverage.Text = "0 sa 0 dk";
        TxtSessionCount.Text = "0";
        TxtDateRange.Text = "";
        TxtWeeklyThisWeek.Text = Strings.Get("Stats_ThisWeek");
        TxtWeeklyLastWeek.Text = Strings.Get("Stats_LastWeek");
        TxtWeeklyDiff.Text = Strings.Get("Stats_Diff");
        _appItems.Clear();
        TimeDistributionChart.Series = new ObservableCollection<ISeries>();
        AppDistributionChart.Series = new ObservableCollection<ISeries>();
    }

    private void SetupTimeChartAxes()
    {
        var textColor = new SKColor(71, 85, 105);
        var separatorColor = new SKColor(226, 232, 240);
        var font = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Normal, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);

        TimeDistributionChart.XAxes = new List<Axis>
        {
            new Axis
            {
                Labeler = v => { var h = (int)Math.Round(v); return h >= 0 && h < 24 ? h.ToString() : ""; },
                LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = font },
                SeparatorsPaint = new SolidColorPaint(separatorColor) { StrokeThickness = 1 },
                MinStep = 1,
                NamePaint = null
            }
        };
        TimeDistributionChart.YAxes = new List<Axis>
        {
            new Axis
            {
                Labeler = v => { var m = (int)v; if (m >= 60) return $"{m / 60} sa"; return m > 0 ? $"{m} dk" : "0 dk"; },
                LabelsPaint = new SolidColorPaint(textColor) { SKTypeface = font },
                SeparatorsPaint = new SolidColorPaint(separatorColor) { StrokeThickness = 1 },
                MinStep = 15,
                NamePaint = null
            }
        };
    }

    private class StatAppItem
    {
        public string DisplayName { get; set; } = "";
        public string TotalFormatted { get; set; } = "";
        public string AverageFormatted { get; set; } = "";
        public string PercentageFormatted { get; set; } = "";
    }
}
