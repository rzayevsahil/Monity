using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Monity.App.Helpers;
using Monity.Infrastructure.Persistence;

namespace Monity.App.Services;

public class InsightService : IInsightService
{
    private readonly IUsageRepository _repository;

    public InsightService(IUsageRepository repository)
    {
        _repository = repository;
    }

    public async Task<List<InsightItem>> GetInsightsAsync(DateTime date, CancellationToken ct = default)
    {
        var insights = new List<InsightItem>();

        try
        {
            // 1. Trend Insight
            var trendInsight = await GetTrendInsightAsync(date, ct);
            if (trendInsight != null) insights.Add(trendInsight);

            // 2. Peak Productivity Hour Insight
            var peakHourInsight = await GetPeakHourInsightAsync(date, ct);
            if (peakHourInsight != null) insights.Add(peakHourInsight);

            // 3. Category Shift Insight
            var shiftInsight = await GetCategoryShiftInsightAsync(date, ct);
            if (shiftInsight != null) insights.Add(shiftInsight);

            // 4. Category-based trend (e.g. "Son 5 günde oyun süren %35 arttı")
            var categoryTrendInsight = await GetCategoryTrendInsightAsync(date, ct);
            if (categoryTrendInsight != null) insights.Add(categoryTrendInsight);
        }
        catch (Exception ex)
        {
            // Silent fail for insights, don't break the dashboard
            System.Diagnostics.Debug.WriteLine($"Insight error: {ex.Message}");
        }

        return insights;
    }

    private async Task<InsightItem?> GetTrendInsightAsync(DateTime date, CancellationToken ct)
    {
        // Compare last 5 days with previous 5 days
        var end1 = date.AddDays(-1);
        var start1 = date.AddDays(-5);
        var end2 = date.AddDays(-6);
        var start2 = date.AddDays(-10);

        var period1 = await _repository.GetRangeTotalAsync(start1, end1, ct: ct);
        var period2 = await _repository.GetRangeTotalAsync(start2, end2, ct: ct);

        if (period1.TotalSeconds > 3600 && period2.TotalSeconds > 0) 
        {
            var diff = period1.TotalSeconds - period2.TotalSeconds;
            var percent = (double)Math.Abs(diff) / period2.TotalSeconds * 100;

            if (percent >= 15) // Significant change
            {
                string key = diff > 0 ? "Insight_Trend_Increase" : "Insight_Trend_Decrease";
                string message = string.Format(Strings.Get(key), Math.Round(percent, 0));
                return new InsightItem(message, diff > 0 ? "TrendingUp" : "TrendingDown", "Trend");
            }
        }

        return null;
    }

    private async Task<InsightItem?> GetPeakHourInsightAsync(DateTime date, CancellationToken ct)
    {
        var hourly = await _repository.GetHourlyUsageAsync(date.ToString("yyyy-MM-dd"), ct: ct);
        if (hourly.Count < 2) return null;

        // Find best 2-hour window
        long maxSeconds = 0;
        int bestStartHour = -1;

        for (int i = 0; i < 23; i++)
        {
            var h1 = hourly.FirstOrDefault(x => x.Hour == i)?.TotalSeconds ?? 0;
            var h2 = hourly.FirstOrDefault(x => x.Hour == i + 1)?.TotalSeconds ?? 0;
            if (h1 + h2 > maxSeconds)
            {
                maxSeconds = h1 + h2;
                bestStartHour = i;
            }
        }

        if (maxSeconds > 3600) // At least 1 hour of total usage in that 2hr window
        {
            string message = string.Format(Strings.Get("Insight_PeakHours"), $"{bestStartHour:00}:00", $"{bestStartHour + 2:00}:00");
            return new InsightItem(message, "LightningBolt", "Productivity");
        }

        return null;
    }

    private async Task<InsightItem?> GetCategoryShiftInsightAsync(DateTime date, CancellationToken ct)
    {
        var todayCategories = await _repository.GetCategoryUsageInRangeAsync(date, date, ct);
        if (!todayCategories.Any()) return null;

        var topCategory = todayCategories.First();
        if (topCategory.TotalSeconds < 1800) return null; // Minor usage

        // Compare with last 7 days average for this category
        var last7Start = date.AddDays(-7);
        var last7End = date.AddDays(-1);
        var last7Usage = await _repository.GetCategoryUsageInRangeAsync(last7Start, last7End, ct);
        
        var history = last7Usage.FirstOrDefault(x => x.CategoryName == topCategory.CategoryName);
        var avgSeconds = (history?.TotalSeconds ?? 0) / 7;

        if (avgSeconds > 0)
        {
            var ratio = (double)topCategory.TotalSeconds / avgSeconds;
            if (ratio > 1.5) // 50% more than average
            {
                string categoryDisplayName = GetCategoryDisplayName(topCategory.CategoryName);
                string message = string.Format(Strings.Get("Insight_CategoryShift"), categoryDisplayName);
                return new InsightItem(message, "Star", "Observation");
            }
        }

        return null;
    }

    private string GetCategoryDisplayName(string categoryName)
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

    /// <summary>
    /// Compare last 5 days vs previous 5 days per category; return insight for significant change (e.g. Eğlence/Oyun).
    /// </summary>
    private async Task<InsightItem?> GetCategoryTrendInsightAsync(DateTime date, CancellationToken ct)
    {
        var categoriesToCheck = new[] { "Eğlence", "Geliştirme", "Sosyal", "Tarayıcı", "Ofis" };
        var end1 = date.AddDays(-1);
        var start1 = date.AddDays(-5);
        var end2 = date.AddDays(-6);
        var start2 = date.AddDays(-10);

        foreach (var cat in categoriesToCheck)
        {
            var period1 = await _repository.GetCategoryUsageInRangeAsync(start1, end1, ct);
            var period2 = await _repository.GetCategoryUsageInRangeAsync(start2, end2, ct);
            var sum1 = period1.Where(x => x.CategoryName == cat).Sum(x => x.TotalSeconds);
            var sum2 = period2.Where(x => x.CategoryName == cat).Sum(x => x.TotalSeconds);
            if (sum2 == 0 || sum1 < 1800) continue;
            var diff = sum1 - sum2;
            var percent = (double)Math.Abs(diff) / sum2 * 100;
            if (percent >= 15)
            {
                var categoryDisplayName = GetCategoryDisplayName(cat);
                var key = diff > 0 ? "Insight_Trend_Category_Increase" : "Insight_Trend_Category_Decrease";
                var message = string.Format(Strings.Get(key), categoryDisplayName, Math.Round(percent, 0));
                return new InsightItem(message, diff > 0 ? "TrendingUp" : "TrendingDown", "Trend");
            }
        }
        return null;
    }
}
