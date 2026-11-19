using System.Globalization;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Data.Database;

namespace CaoMinhComputer.Data.Repositories;

/// <summary>Thời gian sử dụng theo ngày và theo phiên của tài khoản con.</summary>
public sealed class UsageRepository(AppDatabase db)
{
    public const int KeepDays = 400;

    public DailyUsage Get(string sid, string day)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT used_seconds, extra_seconds FROM usage_daily WHERE sid=$s AND day=$d";
        cmd.Parameters.AddWithValue("$s", sid);
        cmd.Parameters.AddWithValue("$d", day);
        using var r = cmd.ExecuteReader();
        return r.Read() ? new DailyUsage(sid, day, r.GetInt32(0), r.GetInt32(1)) : new DailyUsage(sid, day, 0, 0);
    }

    public void AddUsed(string sid, string day, int seconds) => Upsert(sid, day, "used_seconds", seconds);

    public void AddExtra(string sid, string day, int seconds) => Upsert(sid, day, "extra_seconds", seconds);

    /// <summary>Đặt lại số giây đã dùng (dùng cho kiểm thử hoặc khi phụ huynh xóa thống kê ngày).</summary>
    public void SetUsed(string sid, string day, int seconds)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            INSERT INTO usage_daily(sid, day, used_seconds, extra_seconds) VALUES($s,$d,$v,0)
            ON CONFLICT(sid, day) DO UPDATE SET used_seconds=$v
            """;
        cmd.Parameters.AddWithValue("$s", sid);
        cmd.Parameters.AddWithValue("$d", day);
        cmd.Parameters.AddWithValue("$v", Math.Max(0, seconds));
        cmd.ExecuteNonQuery();
    }

    private void Upsert(string sid, string day, string column, int seconds)
    {
        if (seconds == 0) return;
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        // column chỉ nhận hằng nội bộ, không lấy từ dữ liệu người dùng.
        cmd.CommandText = $"""
            INSERT INTO usage_daily(sid, day, {column}) VALUES($s,$d,MAX(0,$v))
            ON CONFLICT(sid, day) DO UPDATE SET {column}=MAX(0, {column} + $v)
            """;
        cmd.Parameters.AddWithValue("$s", sid);
        cmd.Parameters.AddWithValue("$d", day);
        cmd.Parameters.AddWithValue("$v", seconds);
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<DailyUsage> GetRange(string fromDay, string toDay)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT sid, day, used_seconds, extra_seconds FROM usage_daily WHERE day >= $f AND day <= $t ORDER BY day";
        cmd.Parameters.AddWithValue("$f", fromDay);
        cmd.Parameters.AddWithValue("$t", toDay);
        using var r = cmd.ExecuteReader();
        var list = new List<DailyUsage>();
        while (r.Read()) list.Add(new DailyUsage(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3)));
        return list;
    }

    public long StartSession(string sid, DateTime startUtc)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO usage_sessions(sid, start_utc, seconds) VALUES($s,$t,0); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$s", sid);
        cmd.Parameters.AddWithValue("$t", startUtc.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void UpdateSession(long id, int seconds, DateTime? endUtc)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE usage_sessions SET seconds=$sec, end_utc=$e WHERE id=$id";
        cmd.Parameters.AddWithValue("$sec", seconds);
        cmd.Parameters.AddWithValue("$e", endUtc is { } e ? e.ToString("O", CultureInfo.InvariantCulture) : DBNull.Value);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Đóng các phiên bị bỏ dở (dịch vụ dừng đột ngột, mất điện): kết thúc = bắt đầu + số giây đã ghi.</summary>
    public int CloseDanglingSessions()
    {
        using var c = db.Open();
        using var select = c.CreateCommand();
        select.CommandText = "SELECT id, start_utc, seconds FROM usage_sessions WHERE end_utc IS NULL";
        var open = new List<(long Id, DateTime Start, int Seconds)>();
        using (var r = select.ExecuteReader())
            while (r.Read())
                open.Add((r.GetInt64(0), DateTime.Parse(r.GetString(1), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind), r.GetInt32(2)));
        foreach (var (id, start, seconds) in open) UpdateSession(id, seconds, start.AddSeconds(seconds));
        return open.Count;
    }

    public IReadOnlyList<UsageSession> RecentSessions(int limit)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, sid, start_utc, end_utc, seconds FROM usage_sessions ORDER BY id DESC LIMIT $l";
        cmd.Parameters.AddWithValue("$l", limit);
        using var r = cmd.ExecuteReader();
        var list = new List<UsageSession>();
        while (r.Read())
        {
            list.Add(new UsageSession
            {
                Id = r.GetInt64(0),
                Sid = r.GetString(1),
                StartUtc = DateTime.Parse(r.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                EndUtc = r.IsDBNull(3) ? null : DateTime.Parse(r.GetString(3), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                Seconds = r.GetInt32(4)
            });
        }
        return list;
    }

    public void Prune()
    {
        var cutoff = DateTime.UtcNow.AddDays(-KeepDays);
        using var c = db.Open();
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM usage_daily WHERE day < $d";
            cmd.Parameters.AddWithValue("$d", cutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }
        using (var cmd = c.CreateCommand())
        {
            cmd.CommandText = "DELETE FROM usage_sessions WHERE start_utc < $t";
            cmd.Parameters.AddWithValue("$t", cutoff.ToString("O", CultureInfo.InvariantCulture));
            cmd.ExecuteNonQuery();
        }
    }
}
