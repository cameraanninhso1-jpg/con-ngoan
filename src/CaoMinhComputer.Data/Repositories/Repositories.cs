using System.Globalization;
using System.Text;
using CaoMinhComputer.Core.Interfaces;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Data.Database;
using Microsoft.Data.Sqlite;

namespace CaoMinhComputer.Data.Repositories;

public static class SettingKeys
{
    public const string ChildSids = "child.sids";
    public const string AppAutoClose = "apps.autoclose";
    public const string ProtectionPaused = "protection.paused";
    public const string ProtectionPausedUntilUtc = "protection.paused_until_utc";
    public const string WebLastApplied = "web.last_applied_utc";
    public const string ServiceHeartbeat = "service.heartbeat_utc";
    public const string FailedLogins = "auth.failed_count";
    public const string LockoutUntil = "auth.lockout_until_utc";
    public const string DisallowRunPrefix = "disallowrun.";
    public const string IdleLockMinutes = "ui.idle_lock_minutes";
    public const string TimeConfig = "time.config";
    public const string TimePausedUntil = "time.paused_until_utc";
    public const string BreakApplied = "break.applied";
    public const string AppLockerStrict = "applocker.strict";
    public const string AppLockerAllowedFolders = "applocker.allowed_folders";
    public const string IdleExclusion = "time.idle_exclusion";
    public const string IdleThresholdSeconds = "time.idle_threshold_seconds";
    public const string Uninstalled = "system.uninstalled";
    public const string WebScope = "web.scope";
    public const string WebScopeBlocking = "web.scope_blocking";
    /// <summary>SID tài khoản con chưa có hồ sơ Windows lúc áp dụng chặn ứng dụng (áp dụng lại khi con đăng nhập lần đầu).</summary>
    public const string ProfilePending = "apps.profile_pending";
    /// <summary>Cập nhật phần mềm: lần kiểm tra gần nhất, bản mới (JSON manifest đã xác minh chữ ký), trạng thái, bản đang cài dở.</summary>
    public const string UpdateLastCheck = "update.last_check_utc";
    public const string UpdateAvailable = "update.available";
    public const string UpdateStatus = "update.status";
    public const string UpdatePending = "update.pending";
    public const string UpdateAutoCheck = "update.auto_check";
    /// <summary>Chính sách trình duyệt: khóa DoH, khóa tiện ích mở rộng, ép buộc SafeSearch.</summary>
    public const string BrowserDisableDoH = "browser.disable_doh";
    public const string BrowserBlockExtensions = "browser.block_extensions";
    public const string BrowserSafeSearch = "browser.safesearch";
    public const string BrowserBlockIncognito = "browser.block_incognito";
    public const string SecurityAntiTamper = "security.anti_tamper";
    /// <summary>Danh sách SID đã bị khóa Task Manager/Regedit/CMD (để gỡ đúng khi bỏ chọn tài khoản).</summary>
    public const string AntiTamperSids = "security.anti_tamper_sids";
    public const string SecuritySuperviseAgent = "security.supervise_agent";
    /// <summary>Điều khiển từ xa qua Telegram Bot.</summary>
    public const string TelegramBotToken = "telegram.bot_token";
    public const string TelegramChatId = "telegram.chat_id";
    public const string TelegramEnabled = "telegram.enabled";
    public const string TelegramNotifyExtraTime = "telegram.notify_extra_time";
    public const string TelegramDailyReport = "telegram.daily_report";
    public const string TelegramDailyReportTime = "telegram.daily_report_time";
    public const string TelegramDailyReportLastDate = "telegram.daily_report_last_date";
    /// <summary>Chặn ứng dụng thời gian thực cho Windows Home (WMI event).</summary>
    public const string RealtimeProcessBlocking = "apps.realtime_blocking";
}

public sealed class SettingsRepository(AppDatabase db)
{
    public string? Get(string key)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT value FROM settings WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        return cmd.ExecuteScalar() as string;
    }

    public void Set(string key, string value)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO settings(key,value) VALUES($k,$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", value);
        cmd.ExecuteNonQuery();
    }

    public void Delete(string key)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM settings WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyDictionary<string, string> GetByPrefix(string prefix)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM settings WHERE substr(key, 1, length($p)) = $p";
        cmd.Parameters.AddWithValue("$p", prefix);
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<string, string>();
        while (r.Read()) d[r.GetString(0)] = r.GetString(1);
        return d;
    }

    public bool GetBool(string key, bool fallback = false) =>
        Get(key) is { } v ? v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) : fallback;

    public void SetBool(string key, bool value) => Set(key, value ? "1" : "0");

    public int GetInt(string key, int fallback = 0) =>
        int.TryParse(Get(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    public DateTime? GetUtc(string key) =>
        DateTime.TryParse(Get(key), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var v) ? v : null;

    public void SetUtc(string key, DateTime value) => Set(key, value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    public IReadOnlyList<string> GetChildSids() =>
        (Get(SettingKeys.ChildSids) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public void SetChildSids(IEnumerable<string> sids) => Set(SettingKeys.ChildSids, string.Join(",", sids.Distinct()));

    public TimeConfig GetTimeConfig() => Core.Models.TimeConfig.FromJson(Get(SettingKeys.TimeConfig));

    public void SaveTimeConfig(TimeConfig config) => Set(SettingKeys.TimeConfig, config.ToJson());
}

public sealed record AdminRecord(string Username, string PasswordHash, DateTime CreatedUtc, DateTime? LastLoginUtc);

public sealed class AdminRepository(AppDatabase db)
{
    public AdminRecord? Get()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT username, password_hash, created_utc, last_login_utc FROM admin WHERE id=1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new AdminRecord(r.GetString(0), r.GetString(1), ParseUtc(r.GetString(2)),
            r.IsDBNull(3) ? null : ParseUtc(r.GetString(3)));
    }

    public bool Exists() => Get() is not null;

    public void Create(string username, string passwordHash)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        var now = DateTime.UtcNow.ToString("O");
        cmd.CommandText = "INSERT INTO admin(id, username, password_hash, created_utc, updated_utc) VALUES(1, $u, $h, $t, $t)";
        cmd.Parameters.AddWithValue("$u", username);
        cmd.Parameters.AddWithValue("$h", passwordHash);
        cmd.Parameters.AddWithValue("$t", now);
        cmd.ExecuteNonQuery();
    }

    public void Update(string username, string passwordHash)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE admin SET username=$u, password_hash=$h, updated_utc=$t WHERE id=1";
        cmd.Parameters.AddWithValue("$u", username);
        cmd.Parameters.AddWithValue("$h", passwordHash);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public void TouchLogin()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE admin SET last_login_utc=$t WHERE id=1";
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Xóa tài khoản quản trị của ứng dụng (chỉ dùng cho quy trình khôi phục bởi Administrator Windows).</summary>
    public void Delete()
    {
        using var c = db.Open();
        AppDatabase.Exec(c, "DELETE FROM admin");
    }

    internal static DateTime ParseUtc(string s) =>
        DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}

public sealed class BlockRepository(AppDatabase db)
{
    public IReadOnlyDictionary<string, bool> GetPlatformStates()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT key, enabled FROM platform_state";
        using var r = cmd.ExecuteReader();
        var d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        while (r.Read()) d[r.GetString(0)] = r.GetInt64(1) != 0;
        return d;
    }

    public IReadOnlyList<string> GetEnabledPlatformKeys() =>
        GetPlatformStates().Where(kv => kv.Value).Select(kv => kv.Key).ToList();

    public IReadOnlyList<string> GetCustomDomains()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT domain FROM custom_domains ORDER BY created_utc, domain";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }

    /// <summary>Lưu toàn bộ cấu hình chặn web trong một transaction.</summary>
    public void Save(IReadOnlyDictionary<string, bool> platformStates, IReadOnlyList<string> customDomains)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        foreach (var (key, enabled) in platformStates)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO platform_state(key, enabled) VALUES($k,$e) ON CONFLICT(key) DO UPDATE SET enabled=excluded.enabled";
            cmd.Parameters.AddWithValue("$k", key);
            cmd.Parameters.AddWithValue("$e", enabled ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        using (var del = c.CreateCommand())
        {
            del.Transaction = tx;
            var names = customDomains.Select((_, i) => "$d" + i).ToList();
            del.CommandText = names.Count == 0
                ? "DELETE FROM custom_domains"
                : $"DELETE FROM custom_domains WHERE domain NOT IN ({string.Join(",", names)})";
            for (var i = 0; i < customDomains.Count; i++) del.Parameters.AddWithValue("$d" + i, customDomains[i]);
            del.ExecuteNonQuery();
        }

        var now = DateTime.UtcNow.ToString("O");
        foreach (var d in customDomains)
        {
            using var cmd = c.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT OR IGNORE INTO custom_domains(domain, created_utc) VALUES($d,$t)";
            cmd.Parameters.AddWithValue("$d", d);
            cmd.Parameters.AddWithValue("$t", now);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}

public sealed class AppRuleRepository(AppDatabase db)
{
    public IReadOnlyList<AppRule> GetAll()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, display_name, process_name, path, enabled, created_utc FROM app_rules ORDER BY display_name COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        var list = new List<AppRule>();
        while (r.Read())
        {
            list.Add(new AppRule
            {
                Id = r.GetInt64(0),
                DisplayName = r.GetString(1),
                ProcessName = r.GetString(2),
                Path = r.IsDBNull(3) ? null : r.GetString(3),
                Enabled = r.GetInt64(4) != 0,
                CreatedUtc = AdminRepository.ParseUtc(r.GetString(5))
            });
        }
        return list;
    }

    public IReadOnlyList<string> GetEnabledProcessNames() =>
        GetAll().Where(a => a.Enabled).Select(a => a.ProcessName).ToList();

    /// <summary>Thêm quy tắc; trả về false nếu tiến trình đã có trong danh sách.</summary>
    public bool Add(string displayName, string processName, string? path, bool enabled)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO app_rules(display_name, process_name, path, enabled, created_utc) VALUES($n,$p,$path,$e,$t)";
        cmd.Parameters.AddWithValue("$n", displayName);
        cmd.Parameters.AddWithValue("$p", processName);
        cmd.Parameters.AddWithValue("$path", (object?)path ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$e", enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        return cmd.ExecuteNonQuery() > 0;
    }

    public void SetEnabled(long id, bool enabled)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE app_rules SET enabled=$e WHERE id=$id";
        cmd.Parameters.AddWithValue("$e", enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public void Delete(long id)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM app_rules WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }
}

public sealed class LogQuery
{
    public DateTime? FromLocal { get; init; }
    public DateTime? ToLocal { get; init; }
    public string? Category { get; init; }
    public string? Search { get; init; }
    public int Limit { get; init; } = 2000;
}

/// <summary>Nhật ký hoạt động. Có giới hạn số dòng/tuổi (log rotation) để database không phình to.</summary>
public sealed class LogRepository(AppDatabase db)
{
    public const int MaxRows = 50_000;
    public const int MaxAgeDays = 180;
    private static int _writesSincePrune;

    public void Add(LogLevel level, string category, string message)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO logs(ts_utc, level, category, message) VALUES($t,$l,$c,$m)";
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$l", (int)level);
        cmd.Parameters.AddWithValue("$c", category);
        cmd.Parameters.AddWithValue("$m", message.Length > 2000 ? message[..2000] : message);
        cmd.ExecuteNonQuery();

        if (Interlocked.Increment(ref _writesSincePrune) >= 200)
        {
            Interlocked.Exchange(ref _writesSincePrune, 0);
            Prune(c);
        }
    }

    public void Info(string category, string message) => Add(LogLevel.Info, category, message);
    public void Warn(string category, string message) => Add(LogLevel.Warning, category, message);
    public void Error(string category, string message) => Add(LogLevel.Error, category, message);

    public IReadOnlyList<LogEntry> Query(LogQuery q)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        var sql = new StringBuilder("SELECT id, ts_utc, level, category, message FROM logs WHERE 1=1");
        if (q.FromLocal is { } from)
        {
            sql.Append(" AND ts_utc >= $from");
            cmd.Parameters.AddWithValue("$from", from.Date.ToUniversalTime().ToString("O"));
        }
        if (q.ToLocal is { } to)
        {
            sql.Append(" AND ts_utc < $to");
            cmd.Parameters.AddWithValue("$to", to.Date.AddDays(1).ToUniversalTime().ToString("O"));
        }
        if (!string.IsNullOrWhiteSpace(q.Category))
        {
            sql.Append(" AND category = $cat");
            cmd.Parameters.AddWithValue("$cat", q.Category);
        }
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            sql.Append(" AND message LIKE $s ESCAPE '\\'");
            var escaped = q.Search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            cmd.Parameters.AddWithValue("$s", "%" + escaped + "%");
        }
        sql.Append(" ORDER BY id DESC LIMIT $lim");
        cmd.Parameters.AddWithValue("$lim", q.Limit);
        cmd.CommandText = sql.ToString();

        using var r = cmd.ExecuteReader();
        var list = new List<LogEntry>();
        while (r.Read())
        {
            list.Add(new LogEntry
            {
                Id = r.GetInt64(0),
                TimestampUtc = AdminRepository.ParseUtc(r.GetString(1)),
                Level = (LogLevel)r.GetInt32(2),
                Category = r.GetString(3),
                Message = r.GetString(4)
            });
        }
        return list;
    }

    public int Count()
    {
        using var c = db.Open();
        return Convert.ToInt32(AppDatabase.Scalar(c, "SELECT COUNT(*) FROM logs"), CultureInfo.InvariantCulture);
    }

    public void Clear()
    {
        using var c = db.Open();
        AppDatabase.Exec(c, "DELETE FROM logs");
        AppDatabase.Exec(c, "PRAGMA wal_checkpoint(TRUNCATE)");
    }

    public void Prune()
    {
        using var c = db.Open();
        Prune(c);
    }

    private static void Prune(SqliteConnection c)
    {
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM logs WHERE ts_utc < $t";
            cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.AddDays(-MaxAgeDays).ToString("O"));
            cmd.ExecuteNonQuery();
        }
        AppDatabase.Exec(c, $"DELETE FROM logs WHERE id <= (SELECT id FROM logs ORDER BY id DESC LIMIT 1 OFFSET {MaxRows})");
    }

    /// <summary>Xuất CSV (UTF-8 có BOM để Excel đọc đúng tiếng Việt).</summary>
    public static void ExportCsv(IEnumerable<LogEntry> entries, string path)
    {
        static string Esc(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
        var sb = new StringBuilder();
        sb.AppendLine("Thời gian,Mức,Loại,Nội dung");
        foreach (var e in entries)
            sb.Append(Esc(e.TimestampLocal.ToString("yyyy-MM-dd HH:mm:ss"))).Append(',')
              .Append(Esc(e.LevelText)).Append(',')
              .Append(Esc(e.Category)).Append(',')
              .AppendLine(Esc(e.Message));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
    }
}

public sealed class SystemChangeRepository(AppDatabase db) : ISystemChangeStore
{
    public bool TryGetOriginal(string key, out bool existed, out string? value)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT existed, original_value FROM system_changes WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
        {
            existed = false;
            value = null;
            return false;
        }
        existed = r.GetInt64(0) != 0;
        value = r.IsDBNull(1) ? null : r.GetString(1);
        return true;
    }

    public void SaveOriginalIfAbsent(string key, bool existed, string? value)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO system_changes(key, existed, original_value) VALUES($k,$e,$v)";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$e", existed ? 1 : 0);
        cmd.Parameters.AddWithValue("$v", (object?)value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    public void Remove(string key)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "DELETE FROM system_changes WHERE key=$k";
        cmd.Parameters.AddWithValue("$k", key);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<string> Keys()
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT key FROM system_changes";
        using var r = cmd.ExecuteReader();
        var list = new List<string>();
        while (r.Read()) list.Add(r.GetString(0));
        return list;
    }
}
