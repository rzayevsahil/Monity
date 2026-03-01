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
using Monity.App.Helpers;
using Monity.Infrastructure.Persistence;
using Monity.Infrastructure.Tracking;
using SkiaSharp;

namespace Monity.App.Views;

public partial class BrowserTrackingPage : Page
{
    private readonly IServiceProvider _services;
    private readonly IUsageRepository _repository;
    private readonly UsageTrackingService _trackingService;
    private readonly ObservableCollection<BrowserDomainUsageItem> _domainItems = [];
    private ICollectionView _domainListView = null!;
    private bool _isLoading;

    public BrowserTrackingPage(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        _repository = services.GetRequiredService<IUsageRepository>();
        _trackingService = services.GetRequiredService<UsageTrackingService>();

        DatePicker.SelectedDate = DateTime.Today;
        _domainListView = CollectionViewSource.GetDefaultView(_domainItems);
        DomainListView.ItemsSource = _domainListView;
        SetupBrowserFilter();
        SetupSearchFilter();
        SetupChartAxes();

        Loaded += async (_, _) => await LoadDataAsync();
    }

    private void SetupBrowserFilter()
    {
        var options = new List<BrowserFilterOption>
        {
            new(null, Strings.Get("BrowserTracking_AllBrowsers")),
            new("chrome", "Chrome"),
            new("firefox", "Firefox"),
            new("edge", "Edge"),
            new("safari", "Safari"),
            new("opera", "Opera"),
            new("operagx", "Opera GX")
        };
        BrowserFilter.ItemsSource = options;
        BrowserFilter.DisplayMemberPath = "Display";
        BrowserFilter.SelectedValuePath = "Value";
        BrowserFilter.SelectedIndex = 0;
    }

    private void SetupSearchFilter()
    {
        _domainListView.Filter = item =>
        {
            if (item is not BrowserDomainUsageItem domainItem)
                return false;

            var searchText = DomainSearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(searchText))
                return true;

            return domainItem.Domain.Contains(searchText, StringComparison.OrdinalIgnoreCase);
        };

        // Handle placeholder visibility
        DomainSearchBox.TextChanged += (_, _) =>
        {
            DomainSearchPlaceholder.Visibility = string.IsNullOrEmpty(DomainSearchBox.Text) 
                ? Visibility.Visible 
                : Visibility.Collapsed;
        };
    }

    private void SetupChartAxes()
    {
        // Top Domains Chart
        TopDomainsChart.XAxes = new[]
        {
            new Axis
            {
                Name = Strings.Get("BrowserTracking_Time") + " " + Strings.Get("BrowserTracking_TimeUnit"),
                NamePaint = new SolidColorPaint(SKColors.Gray),
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 12,
                SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) { StrokeThickness = 1 },
                Labeler = value => $"{value:F0}sn"
            }
        };

        TopDomainsChart.YAxes = new[]
        {
            new Axis
            {
                Name = Strings.Get("BrowserTracking_Domain"),
                NamePaint = new SolidColorPaint(SKColors.Gray),
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 12,
                SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) { StrokeThickness = 1 }
            }
        };

        // Hourly Browser Chart
        HourlyBrowserChart.XAxes = new[]
        {
            new Axis
            {
                Name = Strings.Get("BrowserTracking_Hour"),
                NamePaint = new SolidColorPaint(SKColors.Gray),
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 12,
                SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) { StrokeThickness = 1 },
                MinLimit = 0,
                MaxLimit = 23
            }
        };

        HourlyBrowserChart.YAxes = new[]
        {
            new Axis
            {
                Name = Strings.Get("BrowserTracking_Time"),
                NamePaint = new SolidColorPaint(SKColors.Gray),
                LabelsPaint = new SolidColorPaint(SKColors.Gray),
                TextSize = 12,
                SeparatorsPaint = new SolidColorPaint(SKColors.LightGray) { StrokeThickness = 1 },
                Labeler = value => TimeSpan.FromSeconds(value).ToString(@"h\:mm")
            }
        };
    }

    private string GetSelectedDateString()
    {
        return DatePicker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) 
               ?? DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private string? GetSelectedBrowser()
    {
        return BrowserFilter.SelectedItem is BrowserFilterOption opt ? opt.Value : null;
    }

    private async Task LoadDataAsync()
    {
        if (_isLoading)
            return;

        _isLoading = true;
        
        try
        {
            // Flush buffer to ensure we have the latest data
            await _trackingService.FlushBufferAsync();

            var selectedDate = GetSelectedDateString();
            var selectedBrowser = GetSelectedBrowser();

            // Load domain usage data
            var domainUsage = await _repository.GetBrowserDomainUsageAsync(selectedDate, selectedBrowser);

            // Update summary cards with animation-like effect
            await UpdateSummaryCards(domainUsage);

            // Update charts and lists
            UpdateTopDomainsChart(domainUsage.Take(10));
            await UpdateHourlyChart(selectedDate, selectedBrowser);
            UpdateDomainList(domainUsage);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error loading browser tracking data");
            
            // Show user-friendly error
            TxtTotalBrowseTime.Text = "Hata";
            TxtUniqueDomains.Text = "0";
            TxtPageViews.Text = "0";
        }
        finally
        {
            _isLoading = false;
        }
    }

    private async Task UpdateSummaryCards(List<BrowserDomainUsage> domainUsage)
    {
        // Clear current values first for visual feedback
        TxtTotalBrowseTime.Text = "Yükleniyor...";
        TxtUniqueDomains.Text = "...";
        TxtPageViews.Text = "...";

        // Small delay for visual feedback
        await Task.Delay(100);

        // Update with actual values
        var totalSeconds = domainUsage.Sum(d => d.TotalSeconds);
        TxtTotalBrowseTime.Text = FormatDuration((int)totalSeconds);
        TxtUniqueDomains.Text = domainUsage.Count.ToString();
        TxtPageViews.Text = domainUsage.Sum(d => d.PageViews).ToString();
    }

    private void UpdateTopDomainsChart(IEnumerable<BrowserDomainUsage> topDomains)
    {
        try
        {
            var series = new List<ISeries>();
            var domainData = topDomains.ToList();
            
            if (domainData.Count > 0)
            {
                var rowSeries = new RowSeries<BrowserDomainUsage>
                {
                    Values = domainData,
                    YToolTipLabelFormatter = point => point.Model?.Domain ?? "",
                    XToolTipLabelFormatter = point => FormatDuration((int)(point.Model?.TotalSeconds ?? 0)),
                    DataLabelsPaint = new SolidColorPaint(SKColors.Gray),
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.DataLabelsPosition.Top,
                    DataLabelsFormatter = point => FormatDuration((int)(point.Model?.TotalSeconds ?? 0)),
                    Mapping = (domain, index) => new(index, domain.TotalSeconds),
                    Fill = new SolidColorPaint(SKColor.Parse("#3498db"))
                };

                series.Add(rowSeries);
            }

            // Update chart with animation
            TopDomainsChart.Series = series;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error updating top domains chart");
        }
    }

    private async Task UpdateHourlyChart(string selectedDate, string? selectedBrowser)
    {
        try
        {
            var hourlyData = await _repository.GetBrowserHourlyUsageAsync(selectedDate, selectedBrowser);
            var series = new List<ISeries>();
            
            if (hourlyData.Count > 0)
            {
                var columnSeries = new ColumnSeries<BrowserHourlyUsage>
                {
                    Values = hourlyData,
                    XToolTipLabelFormatter = point => $"{point.Model?.Hour:00}:00",
                    YToolTipLabelFormatter = point => FormatDuration((int)(point.Model?.TotalSeconds ?? 0)),
                    Mapping = (usage, index) => new(usage.Hour, usage.TotalSeconds),
                    Fill = new SolidColorPaint(SKColor.Parse("#2ecc71"))
                };

                series.Add(columnSeries);
            }

            // Update chart with proper error handling
            HourlyBrowserChart.Series = series;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Error updating hourly chart");
        }
    }

    private void UpdateDomainList(List<BrowserDomainUsage> domainUsage)
    {
        // Clear existing items
        _domainItems.Clear();
        
        if (domainUsage.Count == 0)
        {
            // Add a placeholder item when no data
            _domainItems.Add(new BrowserDomainUsageItem(
                0,
                "Veri bulunamadı",
                "0 dk",
                0,
                0
            ));
            return;
        }
        
        var totalSeconds = domainUsage.Sum(d => d.TotalSeconds);
        
        // Add items with proper ranking
        for (int i = 0; i < domainUsage.Count; i++)
        {
            var domain = domainUsage[i];
            var percentage = totalSeconds > 0 ? (double)domain.TotalSeconds / totalSeconds * 100 : 0;
            
            _domainItems.Add(new BrowserDomainUsageItem(
                i + 1,
                domain.Domain,
                FormatDuration((int)domain.TotalSeconds),
                domain.SessionCount,
                percentage)
            {
                TotalSeconds = domain.TotalSeconds
            });
        }
        
        // Refresh the view to ensure proper display
        _domainListView.Refresh();
        ApplyDomainListSort();
    }

    private static string FormatDuration(int seconds)
    {
        var timeSpan = TimeSpan.FromSeconds(seconds);
        if (timeSpan.TotalHours >= 1)
            return $"{(int)timeSpan.TotalHours} sa {timeSpan.Minutes} dk";
        else if (timeSpan.TotalMinutes >= 1)
            return $"{timeSpan.Minutes} dk {timeSpan.Seconds} sn";
        else
            return $"{timeSpan.Seconds} sn";
    }

    private async void DatePicker_SelectedDateChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading)
            await LoadDataAsync();
    }

    private async void BrowserFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoading)
            await LoadDataAsync();
    }

    private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        BtnRefresh.IsEnabled = false;
        var originalContent = BtnRefresh.Content;
        
        try
        {
            // Show refreshing state immediately
            BtnRefresh.Content = Strings.Get("Dashboard_Refreshing");
            
            // Force focus to button to commit any pending DatePicker changes
            BtnRefresh.Focus();
            
            // Small delay to show the refreshing state
            await Task.Delay(200);
            
            await LoadDataAsync();
            
            // Show completion feedback briefly
            BtnRefresh.Content = "✓ Tamamlandı";
            await Task.Delay(800);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Error refreshing browser tracking data");
            BtnRefresh.Content = "⚠ Hata";
            await Task.Delay(1500);
        }
        finally
        {
            BtnRefresh.Content = originalContent;
            BtnRefresh.IsEnabled = true;
        }
    }

    private void BtnBack_Click(object sender, RoutedEventArgs e)
    {
        NavigationService?.GoBack();
    }

    private void DomainSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _domainListView.Refresh();
        DomainSearchPlaceholder.Visibility = string.IsNullOrEmpty(DomainSearchBox.Text) 
            ? Visibility.Visible 
            : Visibility.Collapsed;
    }

    private string _domainListSortProperty = nameof(BrowserDomainUsageItem.TotalSeconds);
    private ListSortDirection _domainListSortDirection = ListSortDirection.Descending;

    private void DomainListColumnHeader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not GridViewColumnHeader header || header.Tag is not string tag) return;
        var prop = tag switch
        {
            "Domain" => nameof(BrowserDomainUsageItem.Domain),
            "TotalSeconds" => nameof(BrowserDomainUsageItem.TotalSeconds),
            "Visits" => nameof(BrowserDomainUsageItem.Visits),
            "Percentage" => nameof(BrowserDomainUsageItem.Percentage),
            _ => _domainListSortProperty
        };
        if (prop == _domainListSortProperty)
            _domainListSortDirection = _domainListSortDirection == ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
        else
        {
            _domainListSortProperty = prop;
            _domainListSortDirection = prop == nameof(BrowserDomainUsageItem.Domain) ? ListSortDirection.Ascending : ListSortDirection.Descending;
        }
        ApplyDomainListSort();
    }

    private void ApplyDomainListSort()
    {
        _domainListView.SortDescriptions.Clear();
        _domainListView.SortDescriptions.Add(new SortDescription(_domainListSortProperty, _domainListSortDirection));
    }

    private void DomainListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Clear selection to prevent items staying selected
        if (sender is System.Windows.Controls.ListView listView)
            listView.SelectedItem = null;
    }

    private record BrowserFilterOption(string? Value, string Display);
    
    private record BrowserDomainUsageItem(
        int Rank,
        string Domain,
        string FormattedTime,
        long Visits,
        double Percentage)
    {
        public long TotalSeconds { get; init; }
    }
}