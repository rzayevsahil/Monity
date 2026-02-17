using Monity.Domain.Entities;

namespace Monity.Infrastructure.Persistence;

public interface IUsageRepository
{
    Task<int> GetOrCreateAppIdAsync(string processName, string exePath, CancellationToken ct = default);
    Task AddSessionAsync(UsageSession session, CancellationToken ct = default);
    Task AddSessionsAsync(IReadOnlyList<UsageSession> sessions, CancellationToken ct = default);
    Task UpdateDailySummaryAsync(string date, CancellationToken ct = default);
    Task<IReadOnlyList<AppUsageSummary>> GetDailyUsageAsync(string date, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, CancellationToken ct = default);
    Task<IReadOnlyList<AppUsageSummary>> GetWeeklyUsageAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, CancellationToken ct = default);
    Task<IReadOnlyList<HourlyUsage>> GetHourlyUsageAsync(string date, bool excludeIdle = true, CancellationToken ct = default);
    Task<DailyTotal> GetDailyTotalAsync(string date, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, CancellationToken ct = default);
    Task<DailyTotal> GetRangeTotalAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, CancellationToken ct = default);
    Task<IReadOnlyList<DailyTotalByDate>> GetDailyTotalsInRangeAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, CancellationToken ct = default);
    Task<DateTime?> GetFirstSessionStartedAtAsync(string date, CancellationToken ct = default);
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string value, CancellationToken ct = default);
    Task<IReadOnlyList<AppListItem>> GetTrackedAppsAsync(CancellationToken ct = default);
}

public record AppListItem(string ProcessName, string? DisplayName);

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
