using Monity.Domain.Entities;

namespace Monity.Infrastructure.Persistence;

public interface IUsageRepository
{
    Task<int> GetOrCreateAppIdAsync(string processName, string exePath, CancellationToken ct = default);
    Task AddSessionAsync(UsageSession session, CancellationToken ct = default);
    Task AddSessionsAsync(IReadOnlyList<UsageSession> sessions, CancellationToken ct = default);
    Task UpdateDailySummaryAsync(string date, CancellationToken ct = default);
    Task<IReadOnlyList<AppUsageSummary>> GetDailyUsageAsync(string date, bool excludeIdle = true, CancellationToken ct = default);
    Task<IReadOnlyList<AppUsageSummary>> GetWeeklyUsageAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, CancellationToken ct = default);
    Task<IReadOnlyList<HourlyUsage>> GetHourlyUsageAsync(string date, bool excludeIdle = true, CancellationToken ct = default);
    Task<DailyTotal> GetDailyTotalAsync(string date, bool excludeIdle = true, CancellationToken ct = default);
    Task<string?> GetSettingAsync(string key, CancellationToken ct = default);
    Task SetSettingAsync(string key, string value, CancellationToken ct = default);
    Task<IReadOnlyList<AppListItem>> GetTrackedAppsAsync(CancellationToken ct = default);
}

public record AppListItem(string ProcessName, string? DisplayName);
public record AppUsageSummary(long AppId, string ProcessName, string? DisplayName, long TotalSeconds, long SessionCount);
public record HourlyUsage(long Hour, long TotalSeconds);
public record DailyTotal(long TotalSeconds, int SessionCount);
