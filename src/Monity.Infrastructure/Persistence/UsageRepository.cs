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

    public async Task<IReadOnlyList<AppUsageSummary>> GetDailyUsageAsync(string date, bool excludeIdle = true, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();
        await EnsureDisplayNamesFilledAsync(conn);

        var sql = """
            SELECT a.id AS AppId, a.process_name AS ProcessName, a.display_name AS DisplayName,
                   ds.total_seconds AS TotalSeconds,
                   ds.session_count AS SessionCount
            FROM daily_summary ds
            JOIN apps a ON a.id = ds.app_id
            WHERE ds.date = @Date AND ds.total_seconds > 0
            ORDER BY ds.total_seconds DESC
            """;

        var rows = await conn.QueryAsync<AppUsageSummary>(sql, new { Date = date });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<AppUsageSummary>> GetWeeklyUsageAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var idleFilter = excludeIdle ? "AND s.is_idle = 0" : "";
        var sql = $"""
            SELECT a.id AS AppId, a.process_name AS ProcessName, a.display_name AS DisplayName,
                   SUM(CASE WHEN s.is_idle = 0 THEN s.duration_seconds ELSE 0 END) AS TotalSeconds,
                   COUNT(*) AS SessionCount
            FROM usage_sessions s
            JOIN apps a ON a.id = s.app_id
            WHERE s.day_date >= @Start AND s.day_date <= @End {idleFilter}
            GROUP BY a.id, a.process_name, a.display_name
            ORDER BY TotalSeconds DESC
            """;

        var rows = await conn.QueryAsync<AppUsageSummary>(sql, new
        {
            Start = startDate.ToString("yyyy-MM-dd"),
            End = endDate.ToString("yyyy-MM-dd")
        });
        return rows.ToList();
    }

    public async Task<IReadOnlyList<HourlyUsage>> GetHourlyUsageAsync(string date, bool excludeIdle = true, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var idleFilter = excludeIdle ? "AND is_idle = 0" : "";
        var sql = $"""
            SELECT CAST(substr(started_at, 12, 2) AS INTEGER) AS Hour,
                   SUM(duration_seconds) AS TotalSeconds
            FROM usage_sessions
            WHERE day_date = @Date {idleFilter}
            GROUP BY Hour
            ORDER BY Hour
            """;

        var rows = await conn.QueryAsync<HourlyUsage>(sql, new { Date = date });
        return rows.ToList();
    }

    public async Task<DailyTotal> GetDailyTotalAsync(string date, bool excludeIdle = true, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var sql = """
            SELECT COALESCE(SUM(total_seconds), 0) AS TotalSeconds, COALESCE(SUM(session_count), 0) AS SessionCount
            FROM daily_summary
            WHERE date = @Date
            """;

        // O tarihte kayıt yoksa bazı ortamlarda 0 satır döner; tek satır zorunlu değil
        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Date = date });
        if (row == null)
            return new DailyTotal(0, 0);
        return new DailyTotal((long)row.TotalSeconds, (int)row.SessionCount);
    }

    public async Task<DailyTotal> GetRangeTotalAsync(DateTime startDate, DateTime endDate, bool excludeIdle = true, CancellationToken ct = default)
    {
        await using var conn = OpenConnection();

        var sql = """
            SELECT COALESCE(SUM(CASE WHEN is_idle = 0 THEN duration_seconds ELSE 0 END), 0) AS TotalSeconds,
                   COUNT(*) AS SessionCount
            FROM usage_sessions
            WHERE day_date >= @Start AND day_date <= @End
            """;

        var row = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new
        {
            Start = startDate.ToString("yyyy-MM-dd"),
            End = endDate.ToString("yyyy-MM-dd")
        });
        if (row == null)
            return new DailyTotal(0, 0);
        return new DailyTotal((long)row.TotalSeconds, (int)row.SessionCount);
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
}
