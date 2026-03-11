namespace Monity.App.ViewModels;

/// <summary>
/// Data for the share card visual: period label, total duration, top app, optional insight.
/// </summary>
public class ShareCardViewModel
{
    public string PeriodLabel { get; set; } = "";
    public string DateRangeText { get; set; } = "";
    public string TotalDurationText { get; set; } = "";
    public string AvgPerDayText { get; set; } = "";
    public string SessionCountText { get; set; } = "";
    public string TopAppName { get; set; } = "";
    public string TopCategoryName { get; set; } = "";
    public List<string> TopApps { get; set; } = [];
    public List<ShareCardBarItem> TopAppBars { get; set; } = [];
    public List<string> TopCategories { get; set; } = [];
    public string? PrimaryHighlight { get; set; }
    public List<string> Highlights { get; set; } = [];
    public bool HasTrend { get; set; }
    public string TrendText { get; set; } = "";
    public string? PeakHoursText { get; set; }
    public string? GoalStatusText { get; set; }
    public string? AchievementsText { get; set; }
    public bool HasData { get; set; } = true;
    public string AppName { get; set; } = "Monity";
    public string DeveloperName { get; set; } = "";
    public string GitHubDisplay { get; set; } = "";
    public string LinkedInDisplay { get; set; } = "";
}

public record ShareCardBarItem(string Label, double BarWidthPercent);
