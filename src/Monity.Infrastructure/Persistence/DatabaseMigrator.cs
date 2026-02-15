using Microsoft.Data.Sqlite;

namespace Monity.Infrastructure.Persistence;

/// <summary>
/// Creates SQLite schema (apps, usage_sessions, daily_summary).
/// </summary>
public static class DatabaseMigrator
{
    public static void EnsureSchema(SqliteConnection conn)
    {
        conn.Open();

        const string sql = """
            CREATE TABLE IF NOT EXISTS apps (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                process_name TEXT NOT NULL,
                exe_path TEXT NOT NULL,
                display_name TEXT,
                category TEXT,
                is_ignored INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                UNIQUE(process_name, exe_path)
            );

            CREATE TABLE IF NOT EXISTS usage_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                app_id INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                ended_at TEXT NOT NULL,
                duration_seconds INTEGER NOT NULL,
                is_idle INTEGER NOT NULL DEFAULT 0,
                window_title TEXT,
                day_date TEXT NOT NULL,
                FOREIGN KEY (app_id) REFERENCES apps(id)
            );

            CREATE INDEX IF NOT EXISTS ix_usage_sessions_day_date ON usage_sessions(day_date);
            CREATE INDEX IF NOT EXISTS ix_usage_sessions_app_day ON usage_sessions(app_id, day_date);

            CREATE TABLE IF NOT EXISTS daily_summary (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                app_id INTEGER NOT NULL,
                date TEXT NOT NULL,
                total_seconds INTEGER NOT NULL DEFAULT 0,
                session_count INTEGER NOT NULL DEFAULT 0,
                idle_seconds INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY (app_id) REFERENCES apps(id),
                UNIQUE(app_id, date)
            );

            CREATE INDEX IF NOT EXISTS ix_daily_summary_date ON daily_summary(date);

            CREATE TABLE IF NOT EXISTS app_settings (
                key TEXT PRIMARY KEY,
                value TEXT
            );
            
            INSERT OR IGNORE INTO app_settings (key, value) VALUES ('idle_threshold_seconds', '60');
            """;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
