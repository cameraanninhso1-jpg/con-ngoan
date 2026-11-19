using System.Globalization;
using CaoMinhComputer.Data.Database;

namespace CaoMinhComputer.Data.Repositories;

public sealed class ExtraRequest
{
    public long Id { get; init; }
    public string Sid { get; init; } = "";
    public DateTime CreatedUtc { get; init; }
    public int Minutes { get; init; }
    public string Reason { get; init; } = "";
    public string Status { get; init; } = ExtraRequestRepository.Pending;
    public DateTime? DecidedUtc { get; init; }

    public string AccountName { get; set; } = "";
    public DateTime CreatedLocal => CreatedUtc.ToLocalTime();
    public string Summary => $"{AccountName} xin thêm {Minutes} phút lúc {CreatedLocal:HH:mm}";
    public string ReasonText => string.IsNullOrWhiteSpace(Reason) ? "(không ghi lý do)" : $"Lý do: {Reason}";
}

/// <summary>Yêu cầu xin thêm thời gian do con gửi từ Agent.</summary>
public sealed class ExtraRequestRepository(AppDatabase db)
{
    public const string Pending = "pending";
    public const string Approved = "approved";
    public const string Denied = "denied";
    public const int MaxReasonLength = 200;
    public const int MaxPerDay = 5;
    public static readonly int[] AllowedMinutes = [15, 30, 60];

    public long Create(string sid, int minutes, string reason)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT INTO extra_requests(sid, created_utc, minutes, reason) VALUES($s,$t,$m,$r); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$s", sid);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$m", minutes);
        cmd.Parameters.AddWithValue("$r", reason.Length > MaxReasonLength ? reason[..MaxReasonLength] : reason);
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public IReadOnlyList<ExtraRequest> GetPending() => Query("WHERE status='pending' ORDER BY id", null);

    public ExtraRequest? Latest(string sid) => Query("WHERE sid=$s ORDER BY id DESC LIMIT 1", sid).FirstOrDefault();

    public ExtraRequest? Get(long id)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, sid, created_utc, minutes, reason, status, decided_utc FROM extra_requests WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id);
        using var r = cmd.ExecuteReader();
        return r.Read() ? Map(r) : null;
    }

    public int CountSince(string sid, DateTime sinceUtc)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM extra_requests WHERE sid=$s AND created_utc >= $t";
        cmd.Parameters.AddWithValue("$s", sid);
        cmd.Parameters.AddWithValue("$t", sinceUtc.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Đánh dấu đã duyệt/từ chối. Trả về false nếu yêu cầu không còn ở trạng thái chờ.</summary>
    public bool Decide(long id, bool approved)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "UPDATE extra_requests SET status=$st, decided_utc=$t WHERE id=$id AND status='pending'";
        cmd.Parameters.AddWithValue("$st", approved ? Approved : Denied);
        cmd.Parameters.AddWithValue("$t", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        cmd.Parameters.AddWithValue("$id", id);
        return cmd.ExecuteNonQuery() == 1;
    }

    private IReadOnlyList<ExtraRequest> Query(string where, string? sid)
    {
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT id, sid, created_utc, minutes, reason, status, decided_utc FROM extra_requests " + where;
        if (sid is not null) cmd.Parameters.AddWithValue("$s", sid);
        using var r = cmd.ExecuteReader();
        var list = new List<ExtraRequest>();
        while (r.Read()) list.Add(Map(r));
        return list;
    }

    private static ExtraRequest Map(Microsoft.Data.Sqlite.SqliteDataReader r) => new()
    {
        Id = r.GetInt64(0),
        Sid = r.GetString(1),
        CreatedUtc = DateTime.Parse(r.GetString(2), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        Minutes = r.GetInt32(3),
        Reason = r.GetString(4),
        Status = r.GetString(5),
        DecidedUtc = r.IsDBNull(6) ? null : DateTime.Parse(r.GetString(6), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
    };
}
