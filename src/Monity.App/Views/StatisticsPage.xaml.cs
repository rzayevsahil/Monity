using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Monity.App.Helpers;
using Monity.Infrastructure.Persistence;

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

        Loaded += async (_, _) => await LoadDataAsync();
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

    private void Period_Changed(object sender, RoutedEventArgs e) => _ = LoadDataAsync();

    private async void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e) => await LoadDataAsync();

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
        var date = GetSelectedDate();
        var (start, end, dayCount) = DurationAndPeriodHelper.GetPeriodRange(period, date);

        try
        {
            var totalTask = _repository.GetRangeTotalAsync(start, end, excludeIdle: true);
            var appsTask = _repository.GetWeeklyUsageAsync(start, end, excludeIdle: true);
            await System.Threading.Tasks.Task.WhenAll(totalTask, appsTask);

            var total = await totalTask;
            var apps = await appsTask;

            await Dispatcher.InvokeAsync(() => ApplyData(total.TotalSeconds, total.SessionCount, apps, dayCount));
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Failed to load statistics");
            await Dispatcher.InvokeAsync(ClearData);
        }
    }

    private void ApplyData(long totalSeconds, int sessionCount, System.Collections.Generic.IReadOnlyList<AppUsageSummary> apps, int dayCount)
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
    }

    private void ClearData()
    {
        TxtTotal.Text = "0 sa 0 dk";
        TxtAverage.Text = "0 sa 0 dk";
        TxtSessionCount.Text = "0";
        _appItems.Clear();
    }

    private class StatAppItem
    {
        public string DisplayName { get; set; } = "";
        public string TotalFormatted { get; set; } = "";
        public string AverageFormatted { get; set; } = "";
        public string PercentageFormatted { get; set; } = "";
    }
}
