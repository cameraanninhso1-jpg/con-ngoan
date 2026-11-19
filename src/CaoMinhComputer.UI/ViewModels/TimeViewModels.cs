using System.Windows.Media;
using CaoMinhComputer.Core.Models;

namespace CaoMinhComputer.UI.ViewModels;

public sealed record LimitOption(int Minutes, string Text)
{
    public static IReadOnlyList<LimitOption> All { get; } =
    [
        new(0, "Không giới hạn"), new(30, "30 phút"), new(45, "45 phút"), new(60, "1 giờ"), new(90, "1 giờ 30"),
        new(120, "2 giờ"), new(150, "2 giờ 30"), new(180, "3 giờ"), new(240, "4 giờ"), new(300, "5 giờ"), new(360, "6 giờ")
    ];
}

/// <summary>Một dòng lịch theo ngày trên màn hình Thời gian sử dụng.</summary>
public sealed class DayRowViewModel(int dayIndex, string name, DaySchedule day, Action onChanged) : ObservableObject
{
    private bool _allowed = day.Allowed;
    private int _limitMinutes = LimitOption.All.Any(o => o.Minutes == day.LimitMinutes) ? day.LimitMinutes : day.LimitMinutes;
    private string _from = day.From;
    private string _to = day.To;

    public int DayIndex { get; } = dayIndex;
    public string Name { get; } = name;
    public IReadOnlyList<LimitOption> Options { get; } = LimitOption.All.Any(o => o.Minutes == day.LimitMinutes)
        ? LimitOption.All
        : [.. LimitOption.All, new LimitOption(day.LimitMinutes, $"{day.LimitMinutes} phút")];

    public bool Allowed { get => _allowed; set { if (Set(ref _allowed, value)) onChanged(); } }
    public int LimitMinutes { get => _limitMinutes; set { if (Set(ref _limitMinutes, value)) onChanged(); } }
    public string From { get => _from; set { if (Set(ref _from, value)) onChanged(); } }
    public string To { get => _to; set { if (Set(ref _to, value)) onChanged(); } }

    public DaySchedule ToModel() => new() { Allowed = Allowed, LimitMinutes = LimitMinutes, From = From.Trim(), To = To.Trim() };

    public void CopyFrom(DayRowViewModel other)
    {
        Allowed = other.Allowed;
        LimitMinutes = other.LimitMinutes;
        From = other.From;
        To = other.To;
    }
}

/// <summary>Một khung giờ (giờ nghỉ hoặc giờ giải lao).</summary>
public sealed class WindowRowViewModel(string label, TimeWindow? window, Action onChanged) : ObservableObject
{
    private string _from = window?.From ?? "";
    private string _to = window?.To ?? "";

    public string Label { get; } = label;
    public string From { get => _from; set { if (Set(ref _from, value)) { onChanged(); OnPropertyChanged(nameof(StateText)); } } }
    public string To { get => _to; set { if (Set(ref _to, value)) { onChanged(); OnPropertyChanged(nameof(StateText)); } } }

    public string StateText =>
        string.IsNullOrWhiteSpace(From) && string.IsNullOrWhiteSpace(To) ? "Không dùng"
        : Core.Services.TimeOfDayParser.TryParse(From, out _) && Core.Services.TimeOfDayParser.TryParse(To, out _) ? "Đang dùng"
        : "Chưa hợp lệ";

    public TimeWindow ToModel() => new()
    {
        From = Core.Services.TimeOfDayParser.Normalize(From) ?? From.Trim(),
        To = Core.Services.TimeOfDayParser.Normalize(To) ?? To.Trim()
    };
}

/// <summary>Tình trạng hôm nay của một tài khoản con.</summary>
public sealed class ChildTimeViewModel : ObservableObject
{
    public required string Sid { get; init; }
    public required string Name { get; init; }
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";
    public required string Description { get; init; }
    public required string UsedText { get; init; }
    public required double UsedMinutes { get; init; }
    public required double LimitMinutes { get; init; }
    public bool HasLimit => LimitMinutes > 0;
    public required bool Allowed { get; init; }
    public required string PresenceText { get; init; }
    public Brush StateBrush => (Brush)System.Windows.Application.Current.FindResource(Allowed ? "SuccessBrush" : "DangerBrush");
}

/// <summary>Một cột trong biểu đồ 7 ngày.</summary>
public sealed record DayBarViewModel(string Label, string ValueText, double BarHeight, bool IsToday);
