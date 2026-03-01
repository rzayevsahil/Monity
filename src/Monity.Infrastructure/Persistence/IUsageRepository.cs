using Monity.Domain.Entities;

namespace Monity.Infrastructure.Persistence;

public interface IUsageRepository
{
    Task<int> GetOrCreateAppIdAsync(string processName, string exePath, CancellationToken ct = default);
    Task AddSessionAsync(UsageSession session, CancellationToken ct = default);
    Task AddSessionsAsync(IReadOnlyList<UsageSession> sessions, CancellationToken ct = default);
    Task UpdateDailySummaryAsync(string date, CancellationToken ct = default);
    Task<IReadOnlyList<AppUsageSummary>> GetDailyUsageAsync(string date, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, string? categoryName = null, CancellationToken ct = default);
    Task<IReadOnlyList<AppUsageSummary>> GetWeeklyUsageAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, string? categoryName = null, CancellationToken ct = default);
    Task<IReadOnlyList<HourlyUsage>> GetHourlyUsageAsync(string date, bool excludeIdle = true, string? categoryName = null, CancellationToken ct = default);
    Task<DailyTotal> GetDailyTotalAsync(string date, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, string? categoryName = null, CancellationToken ct = default);
    Task<DailyTotal> GetRangeTotalAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, string? categoryName = null, CancellationToken ct = default);
    Task<IReadOnlyList<DailyTotalByDate>> GetDailyTotalsInRangeAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, string? categoryName = null, CancellationToken ct = default);
    Task<DateTime?> GetFirstSessionStartedAtAsync(string date, CancellationToken ct = default);
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string value, CancellationToken ct = default);
    Task<IReadOnlyList<AppListItem>> GetTrackedAppsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<AppListItemWithCategory>> GetTrackedAppsWithCategoryAsync(CancellationToken ct = default);
    Task SetAppCategoryAsync(int appId, string? categoryName, CancellationToken ct = default);
    Task<string?> GetProcessNameByAppIdAsync(int appId, CancellationToken ct = default);
    Task<long> GetTodayTotalSecondsForAppIdAsync(int appId, CancellationToken ct = default);
    Task DeleteDataOlderThanAsync(DateTime cutoff, CancellationToken ct = default);
    Task DeleteAllDataAsync(CancellationToken ct = default);

    // Browser session management
    Task<int> AddBrowserSessionAsync(BrowserSession session, CancellationToken ct = default);
    Task UpdateBrowserSessionAsync(int sessionId, DateTime endTime, int durationSeconds, CancellationToken ct = default);
    Task<List<BrowserSession>> GetActiveBrowserSessionsAsync(CancellationToken ct = default);

    // Browser analytics queries
    Task<List<BrowserDomainUsage>> GetBrowserDomainUsageAsync(string date, string? browserName = null, CancellationToken ct = default);
    Task<List<BrowserHourlyUsage>> GetBrowserHourlyUsageAsync(string date, string? domain = null, CancellationToken ct = default);
    Task<Dictionary<string, long>> GetTopDomainsAsync(string date, int limit = 10, CancellationToken ct = default);
    Task UpdateBrowserDailySummaryAsync(string date, CancellationToken ct = default);
}

public record AppListItem(string ProcessName, string? DisplayName);

public record AppListItemWithCategory(int AppId, string ProcessName, string? DisplayName, string? CategoryName);

/// <summary>Dapper materialization: parameterless ctor + settable properties (SQLite INTEGER → long).</summary>
public class AppUsageSummary
{
    public long AppId { get; set; }
    public string ProcessName { get; set; } = "";
    public string? DisplayName { get; set; }
    public long TotalSeconds { get; set; }
    public long SessionCount { get; set; }
}

/// <summary>Dapper materialization: parameterless ctor + settable properties (SQLite INTEGER → long).</summary>
public class HourlyUsage
{
    public long Hour { get; set; }
    public long TotalSeconds { get; set; }
}

public record DailyTotal(long TotalSeconds, int SessionCount);

public record DailyTotalByDate(string Date, long TotalSeconds);
