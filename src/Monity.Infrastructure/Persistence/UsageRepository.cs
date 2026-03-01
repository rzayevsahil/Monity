using Dapper;
using Microsoft.Data.Sqlite;
using Monity.Domain.Entities;
using Monity.Infrastructure;

namespace Monity.Infrastructure.Persistence;

public sealed class UsageRepository : IUsageRepository
{
    private readonly IDatabasePathProvider _pathProvider;
    private readonly string _connectionString;

    public UsageRepository(IDatabasePathProvider pathProvider)
    {
        _pathProvider = pathProvider;
        _connectionString = $"Data Source={_pathProvider.GetDatabasePath()}";
        EnsureDatabase();
    }

    private void EnsureDatabase()
    {
        using var conn = new SqliteConnection(_connectionString);
        DatabaseMigrator.EnsureSchema(conn);
    }

    private static async Task EnsureDisplayNamesFilledAsync(SqliteConnection conn)
    {
        var rows = await conn.QueryAsync<(long Id, string ExePath)>(
            "SELECT id, exe_path FROM apps WHERE display_name IS NULL AND exe_path IS NOT NULL");
        foreach (var (id, exePath) in rows)
        {
            var displayName = AppDisplayNameResolver.GetDisplayNameFromExe(exePath);
            if (!string.IsNullOrEmpty(displayName))
                await conn.ExecuteAsync("UPDATE apps SET display_name = @DisplayName WHERE id = @Id",
                    new { DisplayName = displayName, Id = id });
        }
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public async Task<int> GetOrCreateAppIdAsync(string processName, string exePath, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var path = exePath ?? processName;

        var existing = await conn.QuerySingleOrDefaultAsync<int?>(
            "SELECT id FROM apps WHERE process_name = @ProcessName AND exe_path = @ExePath",
            new { ProcessName = processName, ExePath = path });

        if (existing.HasValue)
            return existing.Value;

        // Aynı uygulama farklı kullanıcı yoluyla (örn. User vs Sahil Rzayev) kaydedilmiş olabilir; exe dosya adı aynıysa mevcut kaydı kullan
        var fileName = Path.GetFileName(path);
        if (!string.IsNullOrEmpty(fileName))
        {
            var sameExe = await conn.QueryAsync<(int Id, string ExePath)>(
                "SELECT id, exe_path FROM apps WHERE process_name = @ProcessName",
                new { ProcessName = processName });
            var match = sameExe.FirstOrDefault(r => string.Equals(Path.GetFileName(r.ExePath), fileName, StringComparison.OrdinalIgnoreCase));
            if (match.Id != 0)
                return match.Id;
        }

        var displayName = AppDisplayNameResolver.GetDisplayNameFromExe(path);

        var now = DateTime.UtcNow.ToString("O");
        var id = await conn.ExecuteScalarAsync<int>(
            """
            INSERT INTO apps (process_name, exe_path, display_name, category, is_ignored, created_at, updated_at)
            VALUES (@ProcessName, @ExePath, @DisplayName, NULL, 0, @Now, @Now);
            SELECT last_insert_rowid();
            """,
            new { ProcessName = processName, ExePath = path, DisplayName = displayName, Now = now });

        return id;
    }

    public async Task AddSessionAsync(UsageSession session, CancellationToken ct = default)
    {
        await AddSessionsAsync([session], ct);
    }

    public async Task AddSessionsAsync(IReadOnlyList<UsageSession> sessions, CancellationToken ct = default)
    {
        if (sessions.Count == 0)
            return;

        await using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            foreach (var s in sessions)
            {
                await conn.ExecuteAsync(
                    """
                    INSERT INTO usage_sessions (app_id, started_at, ended_at, duration_seconds, is_idle, window_title, day_date)
                    VALUES (@AppId, @StartedAt, @EndedAt, @DurationSeconds, @IsIdle, @WindowTitle, @DayDate)
                    """,
                    new
                    {
                        s.AppId,
                        StartedAt = s.StartedAt.ToString("O"),
                        EndedAt = s.EndedAt.ToString("O"),
                        s.DurationSeconds,
                        IsIdle = s.IsIdle ? 1 : 0,
                        s.WindowTitle,
                        s.DayDate
                    },
                    tx);
            }

            var dates = sessions.Select(s => s.DayDate).Distinct().ToList();
            foreach (var d in dates)
            {
                await UpdateDailySummaryForDateAsync(conn, tx, d);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public async Task UpdateDailySummaryAsync(string date, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        await UpdateDailySummaryForDateAsync(conn, tx, date);
        tx.Commit();
    }

    private static async Task UpdateDailySummaryForDateAsync(SqliteConnection conn, SqliteTransaction tx, string date)
    {
        await conn.ExecuteAsync(
            """
            INSERT INTO daily_summary (app_id, date, total_seconds, session_count, idle_seconds)
            SELECT app_id, @Date,
                   SUM(CASE WHEN is_idle = 0 THEN duration_seconds ELSE 0 END),
                   COUNT(*),
                   SUM(CASE WHEN is_idle = 1 THEN duration_seconds ELSE 0 END)
            FROM usage_sessions
            WHERE day_date = @Date
            GROUP BY app_id
            ON CONFLICT(app_id, date) DO UPDATE SET
                total_seconds = excluded.total_seconds,
                session_count = excluded.session_count,
                idle_seconds = excluded.idle_seconds
            """,
            new { Date = date },
            tx);
    }

    public async Task<IReadOnlyList<AppUsageSummary>> GetDailyUsageAsync(string date, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, string? categoryName = null, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        await EnsureDisplayNamesFilledAsync(conn);

        var excludeFilter = excludedProcessNames is { Count: > 0 }
            ? " AND a.process_name NOT IN @Excluded"
            : "";
        var categoryFilter = categoryName != null
            ? (categoryName.Length == 0 ? " AND (a.category IS NULL OR a.category = '')" : " AND a.category = @CategoryName")
            : "";
        var sql = $"""
            SELECT a.id AS AppId, a.process_name AS ProcessName, a.display_name AS DisplayName,
                   ds.total_seconds AS TotalSeconds,
                   ds.session_count AS SessionCount
            FROM daily_summary ds
            JOIN apps a ON a.id = ds.app_id
            WHERE ds.date = @Date AND ds.total_seconds > 0{excludeFilter}{categoryFilter}
            ORDER BY ds.total_seconds DESC
            """;

        object param;
        if (excludedProcessNames is { Count: > 0 } && categoryName != null && categoryName.Length > 0)
            param = new { Date = date, Excluded = excludedProcessNames, CategoryName = categoryName };
        else if (excludedProcessNames is { Count: > 0 })
            param = new { Date = date, Excluded = excludedProcessNames };
        else if (categoryName != null && categoryName.Length > 0)
            param = new { Date = date, CategoryName = categoryName };
        else
            param = new { Date = date };
        var rows = await conn.QueryAsync<AppUsageSummary>(sql, param);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<AppUsageSummary>> GetWeeklyUsageAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, string? categoryName = null, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var idleFilter = excludeIdle ? "AND s.is_idle = 0" : "";
        var excludeFilter = excludedProcessNames is { Count: > 0 }
            ? " AND a.process_name NOT IN @Excluded"
            : "";
        var categoryFilter = categoryName != null
            ? (categoryName.Length == 0 ? " AND (a.category IS NULL OR a.category = '')" : " AND a.category = @CategoryName")
            : "";
        var sql = $"""
            SELECT a.id AS AppId, a.process_name AS ProcessName, a.display_name AS DisplayName,
                   SUM(CASE WHEN s.is_idle = 0 THEN s.duration_seconds ELSE 0 END) AS TotalSeconds,
                   COUNT(*) AS SessionCount
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE s.day_date >= @Start AND s.day_date <= @End {idleFilter}{excludeFilter}{categoryFilter}
            GROUP BY a.id, a.process_name, a.display_name
            ORDER BY TotalSeconds DESC
            """;

        var startStr = startDate.ToString("yyyy-MM-dd");
        var endStr = endDate.ToString("yyyy-MM-dd");
        if (excludedProcessNames is { Count: > 0 } && categoryName != null && categoryName.Length > 0)
        {
            var rows = await conn.QueryAsync<AppUsageSummary>(sql, new { Start = startStr, End = endStr, Excluded = excludedProcessNames, CategoryName = categoryName });
            return rows.ToList();
        }
        if (excludedProcessNames is { Count: > 0 })
        {
            var rows = await conn.QueryAsync<AppUsageSummary>(sql, new { Start = startStr, End = endStr, Excluded = excludedProcessNames });
            return rows.ToList();
        }
        if (categoryName != null && categoryName.Length > 0)
        {
            var rows = await conn.QueryAsync<AppUsageSummary>(sql, new { Start = startStr, End = endStr, CategoryName = categoryName });
            return rows.ToList();
        }
        var allRows = await conn.QueryAsync<AppUsageSummary>(sql, new { Start = startStr, End = endStr });
        return allRows.ToList();
    }

    public async Task<IReadOnlyList<HourlyUsage>> GetHourlyUsageAsync(string date, bool excludeIdle = true, string? categoryName = null, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var idleFilter = excludeIdle ? "AND is_idle = 0" : "";
        var categoryFilter = categoryName != null
            ? (categoryName.Length == 0 ? " AND app_id IN (SELECT id FROM apps WHERE category IS NULL OR category = '')" : " AND app_id IN (SELECT id FROM apps WHERE category = @CategoryName)")
            : "";
        var sql = $"""
            SELECT CAST(substr(started_at, 12, 2) AS INTEGER) AS Hour,
                   SUM(duration_seconds) AS TotalSeconds
            FROM usage_sessions
            WHERE day_date = @Date {idleFilter}{categoryFilter}
            GROUP BY Hour
            ORDER BY Hour
            """;

        object param = categoryName != null && categoryName.Length > 0
            ? new { Date = date, CategoryName = categoryName }
            : new { Date = date };
        var rows = await conn.QueryAsync<HourlyUsage>(sql, param);
        return rows.ToList();
    }

    public async Task<DailyTotal> GetDailyTotalAsync(string date, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, string? categoryName = null, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var excludeFilter = excludedProcessNames is { Count: > 0 }
            ? " AND app_id NOT IN (SELECT id FROM apps WHERE process_name IN @Excluded)"
            : "";
        var categoryFilter = categoryName != null
            ? (categoryName.Length == 0 ? " AND app_id IN (SELECT id FROM apps WHERE category IS NULL OR category = '')" : " AND app_id IN (SELECT id FROM apps WHERE category = @CategoryName)")
            : "";
        var sql = $"""
            SELECT COALESCE(SUM(total_seconds), 0) AS TotalSeconds, COALESCE(SUM(session_count), 0) AS SessionCount
            FROM daily_summary
            WHERE date = @Date{excludeFilter}{categoryFilter}
            """;

        dynamic? row = null;
        if (excludedProcessNames is { Count: > 0 } && categoryName != null && categoryName.Length > 0)
            row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Date = date, Excluded = excludedProcessNames, CategoryName = categoryName });
        else if (excludedProcessNames is { Count: > 0 })
            row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Date = date, Excluded = excludedProcessNames });
        else if (categoryName != null && categoryName.Length > 0)
            row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Date = date, CategoryName = categoryName });
        else
            row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Date = date });
        if (row == null)
            return new DailyTotal(0, 0);
        return new DailyTotal((long)row.TotalSeconds, (int)row.SessionCount);
    }

    public async Task<DailyTotal> GetRangeTotalAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, string? categoryName = null, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var excludeFilter = excludedProcessNames is { Count: > 0 }
            ? " AND app_id NOT IN (SELECT id FROM apps WHERE process_name IN @Excluded)"
            : "";
        var categoryFilter = categoryName != null
            ? (categoryName.Length == 0 ? " AND app_id IN (SELECT id FROM apps WHERE category IS NULL OR category = '')" : " AND app_id IN (SELECT id FROM apps WHERE category = @CategoryName)")
            : "";
        var sql = $"""
            SELECT COALESCE(SUM(CASE WHEN is_idle = 0 THEN duration_seconds ELSE 0 END), 0) AS TotalSeconds,
                   COUNT(*) AS SessionCount
            FROM usage_sessions
            WHERE day_date >= @Start AND day_date <= @End{excludeFilter}{categoryFilter}
            """;

        var startStr = startDate.ToString("yyyy-MM-dd");
        var endStr = endDate.ToString("yyyy-MM-dd");
        dynamic? row = null;
        if (excludedProcessNames is { Count: > 0 } && categoryName != null && categoryName.Length > 0)
            row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Start = startStr, End = endStr, Excluded = excludedProcessNames, CategoryName = categoryName });
        else if (excludedProcessNames is { Count: > 0 })
            row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Start = startStr, End = endStr, Excluded = excludedProcessNames });
        else if (categoryName != null && categoryName.Length > 0)
            row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Start = startStr, End = endStr, CategoryName = categoryName });
        else
            row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Start = startStr, End = endStr });
        if (row == null)
            return new DailyTotal(0, 0);
        return new DailyTotal((long)row.TotalSeconds, (int)row.SessionCount);
    }

    public async Task<IReadOnlyList<DailyTotalByDate>> GetDailyTotalsInRangeAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, IReadOnlyList<string>? excludedProcessNames = null, string? categoryName = null, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var excludeFilter = excludedProcessNames is { Count: > 0 }
            ? " AND app_id NOT IN (SELECT id FROM apps WHERE process_name IN @Excluded)"
            : "";
        var categoryFilter = categoryName != null
            ? (categoryName.Length == 0 ? " AND app_id IN (SELECT id FROM apps WHERE category IS NULL OR category = '')" : " AND app_id IN (SELECT id FROM apps WHERE category = @CategoryName)")
            : "";
        var sql = $"""
            SELECT day_date AS Date,
                   SUM(CASE WHEN is_idle = 0 THEN duration_seconds ELSE 0 END) AS TotalSeconds
            FROM usage_sessions
            WHERE day_date >= @Start AND day_date <= @End{excludeFilter}{categoryFilter}
            GROUP BY day_date
            ORDER BY day_date
            """;

        var startStr = startDate.ToString("yyyy-MM-dd");
        var endStr = endDate.ToString("yyyy-MM-dd");
        if (excludedProcessNames is { Count: > 0 } && categoryName != null && categoryName.Length > 0)
        {
            var rows = await conn.QueryAsync<DailyTotalByDate>(sql, new { Start = startStr, End = endStr, Excluded = excludedProcessNames, CategoryName = categoryName });
            return rows.ToList();
        }
        if (excludedProcessNames is { Count: > 0 })
        {
            var rows = await conn.QueryAsync<DailyTotalByDate>(sql, new { Start = startStr, End = endStr, Excluded = excludedProcessNames });
            return rows.ToList();
        }
        if (categoryName != null && categoryName.Length > 0)
        {
            var rows = await conn.QueryAsync<DailyTotalByDate>(sql, new { Start = startStr, End = endStr, CategoryName = categoryName });
            return rows.ToList();
        }
        var allRows = await conn.QueryAsync<DailyTotalByDate>(sql, new { Start = startStr, End = endStr });
        return allRows.ToList();
    }

    public async Task<DateTime?> GetFirstSessionStartedAtAsync(string date, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        var startedAtStr = await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT MIN(started_at) FROM usage_sessions WHERE day_date = @Date",
            new { Date = date });
        if (string.IsNullOrEmpty(startedAtStr)) return null;
        return DateTime.TryParse(startedAtStr, null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt) ? dt : null;
    }

    public async Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        return await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT value FROM app_settings WHERE key = @Key",
            new { Key = key });
    }

    public async Task SetSettingAsync(string key, string value, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        await conn.ExecuteAsync(
            "INSERT INTO app_settings (key, value) VALUES (@Key, @Value) ON CONFLICT(key) DO UPDATE SET value = @Value",
            new { Key = key, Value = value });
    }

    public async Task<IReadOnlyList<AppListItem>> GetTrackedAppsAsync(CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        var rows = await conn.QueryAsync<AppListItem>(
            "SELECT process_name AS ProcessName, display_name AS DisplayName FROM apps ORDER BY COALESCE(display_name, process_name)");
        return rows.ToList();
    }

    public async Task<IReadOnlyList<AppListItemWithCategory>> GetTrackedAppsWithCategoryAsync(CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        var rows = await conn.QueryAsync<(int AppId, string ProcessName, string? DisplayName, string? CategoryName)>(
            "SELECT id AS AppId, process_name AS ProcessName, display_name AS DisplayName, category AS CategoryName FROM apps ORDER BY COALESCE(display_name, process_name)");
        return rows.Select(r => new AppListItemWithCategory(r.AppId, r.ProcessName, r.DisplayName, r.CategoryName)).ToList();
    }

    public async Task SetAppCategoryAsync(int appId, string? categoryName, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        await conn.ExecuteAsync(
            "UPDATE apps SET category = @CategoryName WHERE id = @AppId",
            new { AppId = appId, CategoryName = categoryName ?? "" });
    }

    public async Task<string?> GetProcessNameByAppIdAsync(int appId, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        return await conn.QuerySingleOrDefaultAsync<string>(
            "SELECT process_name FROM apps WHERE id = @AppId",
            new { AppId = appId });
    }

    public async Task<long> GetTodayTotalSecondsForAppIdAsync(int appId, CancellationToken ct = default)
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        await using var conn = OpenConnection();
        var total = await conn.ExecuteScalarAsync<long?>(
            """
            SELECT COALESCE(SUM(duration_seconds), 0)
            FROM usage_sessions
            WHERE app_id = @AppId AND day_date = @Today AND is_idle = 0
            """,
            new { AppId = appId, Today = today });
        return total ?? 0;
    }

    public async Task DeleteDataOlderThanAsync(DateTime cutoff, CancellationToken ct = default)
    {
        var cutoffStr = cutoff.Date.ToString("yyyy-MM-dd");
        await using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync(
            "DELETE FROM usage_sessions WHERE day_date < @Cutoff",
            new { Cutoff = cutoffStr },
            tx);
        await conn.ExecuteAsync(
            "DELETE FROM daily_summary WHERE date < @Cutoff",
            new { Cutoff = cutoffStr },
            tx);
        await conn.ExecuteAsync(
            "DELETE FROM apps WHERE id NOT IN (SELECT DISTINCT app_id FROM usage_sessions)",
            transaction: tx);
        tx.Commit();
    }

    public async Task DeleteAllDataAsync(CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        await conn.ExecuteAsync("DELETE FROM usage_sessions", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM daily_summary", transaction: tx);
        await conn.ExecuteAsync("DELETE FROM apps", transaction: tx);
        tx.Commit();
    }

    // Browser session management methods
    public async Task<int> AddBrowserSessionAsync(BrowserSession session, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        
        var id = await conn.ExecuteScalarAsync<int>(
            """
            INSERT INTO browser_sessions (browser_name, tab_id, url, domain, title, started_at, ended_at, duration_seconds, is_active, day_date, created_at)
            VALUES (@BrowserName, @TabId, @Url, @Domain, @Title, @StartedAt, @EndedAt, @DurationSeconds, @IsActive, @DayDate, @CreatedAt);
            SELECT last_insert_rowid();
            """,
            new
            {
                session.BrowserName,
                session.TabId,
                session.Url,
                session.Domain,
                session.Title,
                StartedAt = session.StartedAt.ToString("O"),
                EndedAt = session.EndedAt?.ToString("O"),
                session.DurationSeconds,
                IsActive = session.IsActive ? 1 : 0,
                session.DayDate,
                CreatedAt = session.CreatedAt.ToString("O")
            });

        return id;
    }

    public async Task UpdateBrowserSessionAsync(int sessionId, DateTime endTime, int durationSeconds, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        
        await conn.ExecuteAsync(
            """
            UPDATE browser_sessions 
            SET ended_at = @EndTime, duration_seconds = @DurationSeconds, is_active = 0
            WHERE id = @SessionId
            """,
            new
            {
                SessionId = sessionId,
                EndTime = endTime.ToString("O"),
                DurationSeconds = durationSeconds
            });
    }

    public async Task<List<BrowserSession>> GetActiveBrowserSessionsAsync(CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        
        var rows = await conn.QueryAsync<BrowserSessionRaw>(
            """
            SELECT id AS Id, browser_name AS BrowserName, tab_id AS TabId, url AS Url, domain AS Domain, 
                   title AS Title, started_at AS StartedAtStr, ended_at AS EndedAtStr, duration_seconds AS DurationSeconds,
                   is_active AS IsActiveInt, day_date AS DayDate, created_at AS CreatedAtStr
            FROM browser_sessions 
            WHERE is_active = 1
            """);

        return rows.Select(r => new BrowserSession
        {
            Id = r.Id,
            BrowserName = r.BrowserName,
            TabId = r.TabId,
            Url = r.Url,
            Domain = r.Domain,
            Title = r.Title,
            StartedAt = DateTime.Parse(r.StartedAtStr),
            EndedAt = string.IsNullOrEmpty(r.EndedAtStr) ? null : DateTime.Parse(r.EndedAtStr),
            DurationSeconds = r.DurationSeconds,
            IsActive = r.IsActiveInt == 1,
            DayDate = r.DayDate,
            CreatedAt = DateTime.Parse(r.CreatedAtStr)
        }).ToList();
    }

    // Browser analytics queries
    public async Task<List<BrowserDomainUsage>> GetBrowserDomainUsageAsync(string date, string? browserName = null, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var browserFilter = browserName != null ? " AND browser_name = @BrowserName" : "";
        var sql = $"""
            SELECT domain AS Domain, browser_name AS BrowserName,
                   SUM(duration_seconds) AS TotalSeconds,
                   COUNT(*) AS SessionCount,
                   COUNT(*) AS PageViews
            FROM browser_sessions
            WHERE day_date = @Date{browserFilter}
            GROUP BY domain, browser_name
            ORDER BY TotalSeconds DESC
            """;

        object param = browserName != null 
            ? new { Date = date, BrowserName = browserName }
            : new { Date = date };

        var rows = await conn.QueryAsync<BrowserDomainUsage>(sql, param);
        return rows.ToList();
    }

    public async Task<List<BrowserHourlyUsage>> GetBrowserHourlyUsageAsync(string date, string? domain = null, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var domainFilter = domain != null ? " AND domain = @Domain" : "";
        var sql = $"""
            SELECT CAST(substr(started_at, 12, 2) AS INTEGER) AS Hour,
                   SUM(duration_seconds) AS TotalSeconds
            FROM browser_sessions
            WHERE day_date = @Date{domainFilter}
            GROUP BY Hour
            ORDER BY Hour
            """;

        object param = domain != null 
            ? new { Date = date, Domain = domain }
            : new { Date = date };

        var rows = await conn.QueryAsync<BrowserHourlyUsage>(sql, param);
        return rows.ToList();
    }

    public async Task<Dictionary<string, long>> GetTopDomainsAsync(string date, int limit = 10, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        
        var rows = await conn.QueryAsync<(string Domain, long TotalSeconds)>(
            """
            SELECT domain, SUM(duration_seconds) AS TotalSeconds
            FROM browser_sessions
            WHERE day_date = @Date
            GROUP BY domain
            ORDER BY TotalSeconds DESC
            LIMIT @Limit
            """,
            new { Date = date, Limit = limit });

        return rows.ToDictionary(r => r.Domain, r => r.TotalSeconds);
    }

    public async Task UpdateBrowserDailySummaryAsync(string date, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();

        await conn.ExecuteAsync(
            """
            INSERT INTO browser_daily_summary (domain, browser_name, date, total_seconds, session_count, page_views)
            SELECT domain, browser_name, @Date,
                   SUM(duration_seconds),
                   COUNT(*),
                   COUNT(*)
            FROM browser_sessions
            WHERE day_date = @Date
            GROUP BY domain, browser_name
            ON CONFLICT(domain, browser_name, date) DO UPDATE SET
                total_seconds = excluded.total_seconds,
                session_count = excluded.session_count,
                page_views = excluded.page_views
            """,
            new { Date = date },
            tx);

        tx.Commit();
    }

    // Helper class for complex query mapping
    private class BrowserSessionRaw
    {
        public int Id { get; set; }
        public string BrowserName { get; set; } = "";
        public string TabId { get; set; } = "";
        public string Url { get; set; } = "";
        public string Domain { get; set; } = "";
        public string? Title { get; set; }
        public string StartedAtStr { get; set; } = "";
        public string? EndedAtStr { get; set; }
        public int DurationSeconds { get; set; }
        public int IsActiveInt { get; set; }
        public string DayDate { get; set; } = "";
        public string CreatedAtStr { get; set; } = "";
    }
}
