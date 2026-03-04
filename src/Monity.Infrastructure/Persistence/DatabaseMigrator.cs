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

            CREATE TABLE IF NOT EXISTS browser_sessions (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                browser_name TEXT NOT NULL,
                tab_id TEXT NOT NULL,
                url TEXT NOT NULL,
                domain TEXT NOT NULL,
                title TEXT,
                started_at TEXT NOT NULL,
                ended_at TEXT,
                duration_seconds INTEGER DEFAULT 0,
                is_active INTEGER DEFAULT 1,
                day_date TEXT NOT NULL,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS ix_browser_sessions_domain_date ON browser_sessions(domain, day_date);
            CREATE INDEX IF NOT EXISTS ix_browser_sessions_browser_date ON browser_sessions(browser_name, day_date);
            CREATE INDEX IF NOT EXISTS ix_browser_sessions_day_date ON browser_sessions(day_date);

            CREATE TABLE IF NOT EXISTS browser_daily_summary (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                domain TEXT NOT NULL,
                browser_name TEXT NOT NULL,
                date TEXT NOT NULL,
                total_seconds INTEGER DEFAULT 0,
                session_count INTEGER DEFAULT 0,
                page_views INTEGER DEFAULT 0,
                UNIQUE(domain, browser_name, date)
            );

            CREATE TABLE IF NOT EXISTS goals (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                title TEXT NOT NULL,
                target_type INTEGER NOT NULL,
                target_value TEXT NOT NULL,
                limit_type INTEGER NOT NULL,
                limit_seconds INTEGER NOT NULL,
                frequency INTEGER NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL
            );
            
            CREATE TABLE IF NOT EXISTS achievements (
                key TEXT PRIMARY KEY,
                type TEXT NOT NULL,
                goal_value INTEGER NOT NULL,
                is_active INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS user_achievements (
                achievement_key TEXT PRIMARY KEY,
                current_value INTEGER NOT NULL DEFAULT 0,
                is_unlocked INTEGER NOT NULL DEFAULT 0,
                unlocked_at TEXT,
                last_updated_at TEXT NOT NULL,
                FOREIGN KEY (achievement_key) REFERENCES achievements(key)
            );
            
            INSERT OR IGNORE INTO app_settings (key, value) VALUES ('idle_threshold_seconds', '60');

            -- Seed initial achievements
            INSERT OR IGNORE INTO achievements (key, type, goal_value) VALUES ('steady_hand', 'streak', 3);
            INSERT OR IGNORE INTO achievements (key, type, goal_value) VALUES ('deep_focus', 'session_total', 18000); -- 5 hours in seconds
            INSERT OR IGNORE INTO achievements (key, type, goal_value) VALUES ('early_bird', 'streak', 3);
            INSERT OR IGNORE INTO achievements (key, type, goal_value) VALUES ('night_owl', 'session_total', 10800); -- 3 hours in seconds
            INSERT OR IGNORE INTO achievements (key, type, goal_value) VALUES ('balanced_day', 'complex', 1);
            """;

        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
