using System.Globalization;
using CaoMinhComputer.Core.Services;
using Microsoft.Data.Sqlite;

namespace CaoMinhComputer.Data.Database;

/// <summary>SQLite cục bộ. Migration theo PRAGMA user_version. Chế độ WAL để UI và dịch vụ đọc/ghi đồng thời.</summary>
public sealed class AppDatabase
{
    public const int CurrentVersion = 4;

    public string FilePath { get; }
    private readonly string _connectionString;

    public AppDatabase(string? filePath = null)
    {
        FilePath = filePath ?? AppPaths.DatabasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Private,
            DefaultTimeout = 10,
            Pooling = true
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;";
        cmd.ExecuteNonQuery();
        return c;
    }

    public void Migrate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        using var c = Open();
        Exec(c, "PRAGMA journal_mode=WAL;");
        var version = Convert.ToInt32(Scalar(c, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
        if (version >= CurrentVersion) return;

        using var tx = c.BeginTransaction();
        if (version < 1)
        {
            Exec(c, """
                CREATE TABLE IF NOT EXISTS settings(
                    key TEXT PRIMARY KEY,
                    value TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS admin(
                    id INTEGER PRIMARY KEY CHECK(id = 1),
                    username TEXT NOT NULL,
                    password_hash TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    updated_utc TEXT NOT NULL,
                    last_login_utc TEXT);
                CREATE TABLE IF NOT EXISTS platform_state(
                    key TEXT PRIMARY KEY,
                    enabled INTEGER NOT NULL);
                CREATE TABLE IF NOT EXISTS custom_domains(
                    domain TEXT PRIMARY KEY,
                    created_utc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS app_rules(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    display_name TEXT NOT NULL,
                    process_name TEXT NOT NULL COLLATE NOCASE UNIQUE,
                    path TEXT,
                    enabled INTEGER NOT NULL,
                    created_utc TEXT NOT NULL);
                CREATE TABLE IF NOT EXISTS logs(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ts_utc TEXT NOT NULL,
                    level INTEGER NOT NULL,
                    category TEXT NOT NULL,
                    message TEXT NOT NULL);
                CREATE INDEX IF NOT EXISTS ix_logs_ts ON logs(ts_utc);
                CREATE TABLE IF NOT EXISTS system_changes(
                    key TEXT PRIMARY KEY,
                    existed INTEGER NOT NULL,
                    original_value TEXT);
                """, tx);

            var now = DateTime.UtcNow.ToString("O");
            foreach (var (name, process) in PlatformCatalog.SampleApps)
            {
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO app_rules(display_name, process_name, path, enabled, created_utc) VALUES($n, $p, NULL, 0, $t)";
                cmd.Parameters.AddWithValue("$n", name);
                cmd.Parameters.AddWithValue("$p", process);
                cmd.Parameters.AddWithValue("$t", now);
                cmd.ExecuteNonQuery();
            }
        }
        if (version < 2)
        {
            // Giai đoạn 2: thời gian sử dụng theo ngày và theo phiên.
            Exec(c, """
                CREATE TABLE IF NOT EXISTS usage_daily(
                    sid TEXT NOT NULL,
                    day TEXT NOT NULL,
                    used_seconds INTEGER NOT NULL DEFAULT 0,
                    extra_seconds INTEGER NOT NULL DEFAULT 0,
                    PRIMARY KEY(sid, day));
                CREATE TABLE IF NOT EXISTS usage_sessions(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    sid TEXT NOT NULL,
                    start_utc TEXT NOT NULL,
                    end_utc TEXT,
                    seconds INTEGER NOT NULL DEFAULT 0);
                CREATE INDEX IF NOT EXISTS ix_usage_sessions_start ON usage_sessions(start_utc);
                """, tx);
        }
        if (version < 3)
        {
            // Giai đoạn 3: con xin thêm thời gian từ Agent, phụ huynh duyệt trong giao diện.
            Exec(c, """
                CREATE TABLE IF NOT EXISTS extra_requests(
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    sid TEXT NOT NULL,
                    created_utc TEXT NOT NULL,
                    minutes INTEGER NOT NULL,
                    reason TEXT NOT NULL,
                    status TEXT NOT NULL DEFAULT 'pending',
                    decided_utc TEXT);
                CREATE INDEX IF NOT EXISTS ix_extra_requests_status ON extra_requests(status);
                """, tx);
        }
        if (version < 4)
        {
            var now = DateTime.UtcNow.ToString("O");
            foreach (var (name, process) in PlatformCatalog.SampleApps)
            {
                using var cmd = c.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = "INSERT OR IGNORE INTO app_rules(display_name, process_name, path, enabled, created_utc) VALUES($n, $p, NULL, 0, $t)";
                cmd.Parameters.AddWithValue("$n", name);
                cmd.Parameters.AddWithValue("$p", process);
                cmd.Parameters.AddWithValue("$t", now);
                cmd.ExecuteNonQuery();
            }
        }
        Exec(c, $"PRAGMA user_version={CurrentVersion};", tx);
        tx.Commit();
    }

    /// <summary>Kiểm tra toàn vẹn database (PRAGMA integrity_check).</summary>
    public bool CheckIntegrity(out string detail)
    {
        using var c = Open();
        detail = Convert.ToString(Scalar(c, "PRAGMA integrity_check;"), CultureInfo.InvariantCulture) ?? "";
        return detail == "ok";
    }

    internal static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = c.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    internal static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }
}
