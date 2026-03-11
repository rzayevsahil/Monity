using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Monity.App.Helpers;
using Monity.App.ViewModels;
using Monity.App.Views;
using Monity.Infrastructure.Persistence;
using Monity.App;

namespace Monity.App.Services;

public sealed class ShareService : IShareService
{
    private const int ShareCardWidth = 1200;
    private const int ShareCardHeight = 630;
    private const double Dpi = 96.0;
    private const double TrendMinPercent = 5.0;

    private readonly IUsageRepository _repository;
    private readonly IInsightService _insightService;
    private readonly IGoalService _goalService;

    public ShareService(IUsageRepository repository, IInsightService insightService, IGoalService goalService)
    {
        _repository = repository;
        _insightService = insightService;
        _goalService = goalService;
    }

    public async Task<ShareResult> CreateShareCardAsync(ShareContext context, CancellationToken ct = default)
    {
        var (start, end, dayCount, periodLabelKey) = GetDateRangeAndPeriodKey(context.Period);
        var excluded = await GetExcludedProcessesAsync(ct);
        var total = await _repository.GetRangeTotalAsync(start, end, excludeIdle: true, excludedProcessNames: excluded, ct: ct);
        var apps = await _repository.GetWeeklyUsageAsync(start, end, excludeIdle: true, excludedProcessNames: excluded, ct: ct);
        var topApp = apps.FirstOrDefault();
        var topAppName = topApp != null ? (topApp.DisplayName ?? topApp.ProcessName) : "";
        if (topAppName.Length > 40) topAppName = topAppName[..37] + "...";

        var categories = await _repository.GetCategoryUsageInRangeAsync(start, end, ct);
        var topCategory = categories.OrderByDescending(x => x.TotalSeconds).FirstOrDefault();
        var topCategoryName = topCategory != null ? GetCategoryDisplayName(topCategory.CategoryName) : "";
        var top3Categories = categories.OrderByDescending(x => x.TotalSeconds).Take(3)
            .Select(c => $"{GetCategoryDisplayName(c.CategoryName)} · {DurationAndPeriodHelper.FormatDuration(c.TotalSeconds)}")
            .ToList();

        var insights = await _insightService.GetInsightsAsync(end, ct);
        var allHighlights = insights
            .Select(x => x.Message)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();
        var primaryHighlight = allHighlights.FirstOrDefault();
        var highlights = allHighlights.Skip(1).Take(3).ToList();
        var peakHoursInsight = insights.FirstOrDefault(x => x.Type == "Productivity");
        var peakHoursText = peakHoursInsight?.Message;

        var dateRangeText = FormatDateRange(start, end, context.Period);
        var (hasTrend, trendText) = await GetTrendAsync(context.Period, start, end, total.TotalSeconds, excluded ?? [], ct);

        var topAppsList = apps.Take(3).ToList();
        var maxAppSeconds = topAppsList.FirstOrDefault()?.TotalSeconds ?? 1;
        var topAppBars = topAppsList.Select(a =>
        {
            var name = a.DisplayName ?? a.ProcessName;
            if (name.Length > 28) name = name[..25] + "...";
            var label = $"{name} · {DurationAndPeriodHelper.FormatDuration(a.TotalSeconds)}";
            var barWidth = maxAppSeconds > 0 ? 100.0 * a.TotalSeconds / maxAppSeconds : 0;
            return new ShareCardBarItem(label, barWidth);
        }).ToList();
        var topAppsDisplay = topAppsList.Select(a =>
        {
            var name = a.DisplayName ?? a.ProcessName;
            if (name.Length > 28) name = name[..25] + "...";
            return $"{name} · {DurationAndPeriodHelper.FormatDuration(a.TotalSeconds)}";
        }).ToList();

        string? goalStatusText = null;
        try
        {
            var progresses = await _goalService.GetGoalProgressesAsync(end, ct);
            var first = progresses.FirstOrDefault();
            if (first != null)
            {
                if (first.ProgressPercentage >= 100)
                    goalStatusText = Strings.Get("Share_GoalExceeded");
                else
                    goalStatusText = $"{Strings.Get("Share_Goal")}: {first.Goal.Title} {DurationAndPeriodHelper.FormatDuration(first.CurrentSeconds)} / {DurationAndPeriodHelper.FormatDuration(first.Goal.LimitSeconds)}";
            }
        }
        catch { /* optional */ }

        string? achievementsText = null;
        try
        {
            var unlocked = await _repository.GetUserAchievementsAsync(ct);
            var definitions = await _repository.GetAchievementsAsync(ct);
            var totalCount = definitions.Count;
            var unlockedCount = unlocked.Count;
            if (totalCount > 0)
                achievementsText = $"{unlockedCount} / {totalCount} {Strings.Get("Share_Achievements").ToLowerInvariant()}";
            else if (unlockedCount > 0)
                achievementsText = $"{unlockedCount} {Strings.Get("Share_Achievements").ToLowerInvariant()}";
        }
        catch { /* optional */ }

        var durationText = DurationAndPeriodHelper.FormatDuration(total.TotalSeconds);
        var avgPerDaySeconds = dayCount > 0 ? total.TotalSeconds / dayCount : 0;
        var avgText = DurationAndPeriodHelper.FormatDuration(avgPerDaySeconds);
        var periodLabel = Strings.Get(periodLabelKey);

        var vm = new ShareCardViewModel
        {
            PeriodLabel = periodLabel,
            DateRangeText = dateRangeText,
            TotalDurationText = durationText,
            AvgPerDayText = string.Format(Strings.Get("Share_Stat_AvgPerDay"), avgText),
            SessionCountText = string.Format(Strings.Get("Share_Stat_Sessions"), total.SessionCount),
            TopAppName = string.IsNullOrEmpty(topAppName) ? "" : topAppName,
            TopCategoryName = string.IsNullOrEmpty(topCategoryName) ? "" : topCategoryName,
            TopApps = topAppsDisplay,
            TopAppBars = topAppBars,
            TopCategories = top3Categories,
            PrimaryHighlight = primaryHighlight,
            Highlights = highlights,
            HasTrend = hasTrend,
            TrendText = trendText,
            PeakHoursText = peakHoursText,
            GoalStatusText = goalStatusText,
            AchievementsText = achievementsText,
            HasData = total.TotalSeconds > 0,
            AppName = Strings.Get("App_Name"),
            DeveloperName = AboutConfig.DeveloperName,
            GitHubDisplay = AboutConfig.GitHubUrl.Replace("https://", ""),
            LinkedInDisplay = AboutConfig.LinkedInUrl.Replace("https://", "")
        };

        BitmapSource? bitmap = null;
        byte[]? pngBytes = null;
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var card = new ShareCard { DataContext = vm };
            var border = new Border
            {
                Child = card,
                Width = ShareCardWidth,
                Height = ShareCardHeight,
                Background = (System.Windows.Media.Brush)System.Windows.Application.Current.Resources["CardBrush"]!
            };
            border.Measure(new System.Windows.Size(ShareCardWidth, ShareCardHeight));
            border.Arrange(new Rect(0, 0, ShareCardWidth, ShareCardHeight));
            border.UpdateLayout();

            var rtb = new RenderTargetBitmap(
                (int)(ShareCardWidth * Dpi / 96),
                (int)(ShareCardHeight * Dpi / 96),
                Dpi, Dpi, PixelFormats.Pbgra32);
            rtb.Render(border);

            bitmap = rtb;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            pngBytes = ms.ToArray();
        }, DispatcherPriority.Send);

        var caption = BuildCaption(context.Period, durationText, total.TotalSeconds);
        return new ShareResult(bitmap, pngBytes, caption);
    }

    private static (DateTime Start, DateTime End, int DayCount, string PeriodLabelKey) GetDateRangeAndPeriodKey(SharePeriod period)
    {
        var today = DateTime.Today;
        switch (period)
        {
            case SharePeriod.Week:
                var diff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
                var monday = today.AddDays(-diff);
                var sunday = monday.AddDays(6);
                return (monday, sunday, 7, "Share_PeriodWeek");
            case SharePeriod.Month:
                var first = new DateTime(today.Year, today.Month, 1);
                var last = first.AddMonths(1).AddDays(-1);
                return (first, last, (last - first).Days + 1, "Share_PeriodMonth");
            default:
                return (today, today, 1, "Share_PeriodToday");
        }
    }

    private async Task<IReadOnlyList<string>?> GetExcludedProcessesAsync(CancellationToken ct)
    {
        var ignored = await _repository.GetSettingAsync("ignored_processes", ct) ?? "";
        var list = ignored.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        list.Add("Monity.App");
        list.Add("explorer");
        return list;
    }

    private static string BuildCaption(SharePeriod period, string durationText, long totalSeconds)
    {
        var key = totalSeconds <= 0 ? "ShareCaption_NoData" : period switch
        {
            SharePeriod.Today => "ShareCaption_Today",
            SharePeriod.Week => "ShareCaption_Week",
            SharePeriod.Month => "ShareCaption_Month",
            _ => "ShareCaption_Today"
        };
        var template = Strings.Get(key);
        return totalSeconds <= 0 ? template : string.Format(template, durationText);
    }

    private static string GetCategoryDisplayName(string? categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName)) return Strings.Get("Category_Uncategorized");

        var key = categoryName switch
        {
            "Diğer" => "Category_Other",
            "Tarayıcı" => "Category_Browser",
            "Geliştirme" => "Category_Development",
            "Sosyal" => "Category_Social",
            "Eğlence" => "Category_Entertainment",
            "Ofis" => "Category_Office",
            "Eğitim" => "Category_Education",
            _ => null
        };

        return key != null ? Strings.Get(key) : categoryName;
    }

    private static string FormatDateRange(DateTime start, DateTime end, SharePeriod period)
    {
        var ci = CultureInfo.CurrentUICulture;
        if (period == SharePeriod.Today || start.Date == end.Date)
            return start.ToString("d MMM yyyy", ci);
        return $"{start.ToString("d MMM", ci)}–{end.ToString("d MMM yyyy", ci)}";
    }

    private static (DateTime Start, DateTime End) GetPreviousPeriodRange(SharePeriod period, DateTime start, DateTime end)
    {
        switch (period)
        {
            case SharePeriod.Today:
                var yesterday = start.AddDays(-1);
                return (yesterday, yesterday);
            case SharePeriod.Week:
                var days = (int)(end - start).TotalDays + 1;
                return (start.AddDays(-days), end.AddDays(-days));
            case SharePeriod.Month:
                return (start.AddMonths(-1), end.AddMonths(-1));
            default:
                return (start.AddDays(-1), end.AddDays(-1));
        }
    }

    private async Task<(bool HasTrend, string TrendText)> GetTrendAsync(SharePeriod period, DateTime start, DateTime end, long currentTotalSeconds, IReadOnlyList<string> excluded, CancellationToken ct)
    {
        if (currentTotalSeconds <= 0) return (false, "");
        var (startPrev, endPrev) = GetPreviousPeriodRange(period, start, end);
        var prevTotal = await _repository.GetRangeTotalAsync(startPrev, endPrev, excludeIdle: true, excludedProcessNames: excluded, ct: ct);
        if (prevTotal.TotalSeconds <= 0) return (false, "");
        var diff = currentTotalSeconds - prevTotal.TotalSeconds;
        var percent = (double)Math.Abs(diff) / prevTotal.TotalSeconds * 100;
        if (percent < TrendMinPercent) return (false, "");
        var pctStr = Math.Round(percent, 0).ToString(CultureInfo.InvariantCulture);
        var formatted = string.Format(Strings.Get("Share_Trend_VsPrevious"), (diff > 0 ? "+" : "") + pctStr);
        return (true, formatted);
    }
}
