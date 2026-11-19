using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Media;
using CaoMinhComputer.Core.Models;

namespace CaoMinhComputer.UI.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}

public sealed class RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) : ICommand
{
    public RelayCommand(Action execute) : this(_ => execute()) { }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
}

/// <summary>Thông báo kết quả áp dụng (xanh = thành công, đỏ = lỗi) hiển thị trên mỗi màn hình.</summary>
public sealed class StatusMessage : ObservableObject
{
    private string? _text;
    private bool _isError;

    public string? Text
    {
        get => _text;
        private set
        {
            if (Set(ref _text, value)) OnPropertyChanged(nameof(HasText));
        }
    }

    public bool IsError
    {
        get => _isError;
        private set => Set(ref _isError, value);
    }

    public bool HasText => !string.IsNullOrEmpty(Text);

    public void Show(ApplyResult result) => Show(result.Message, !result.Success);

    public void Show(string text, bool isError)
    {
        IsError = isError;
        Text = text;
    }

    public void Clear() => Text = null;
}

public sealed class PlatformItemViewModel(PlatformDefinition definition, Action onChanged) : ObservableObject
{
    private bool _isBlocked;

    public PlatformDefinition Definition { get; } = definition;
    public string Key => Definition.Key;
    public string Name => Definition.Name;
    public string Badge => Definition.Badge;
    public Brush BadgeBrush { get; } = (SolidColorBrush)new BrushConverter().ConvertFromString(definition.Color)!;
    public string Tooltip => "Tên miền chặn: " + string.Join(", ", Definition.Domains);

    public bool IsBlocked
    {
        get => _isBlocked;
        set
        {
            if (Set(ref _isBlocked, value)) onChanged();
        }
    }

    /// <summary>Đặt giá trị mà không đánh dấu "chưa lưu" (dùng khi tải từ database).</summary>
    public void Load(bool value)
    {
        _isBlocked = value;
        OnPropertyChanged(nameof(IsBlocked));
    }
}

public sealed class AppRuleViewModel(AppRule rule, Action onChanged) : ObservableObject
{
    private bool _enabled = rule.Enabled;

    public AppRule Rule { get; } = rule;
    public long Id => Rule.Id;
    public string DisplayName => Rule.DisplayName;
    public string ProcessName => Rule.ProcessName;
    public string PathText => string.IsNullOrEmpty(Rule.Path) ? "Nhận diện theo tên tiến trình" : Rule.Path!;
    public string Badge => GetBadge(DisplayName, ProcessName);
    public Brush BadgeBrush => GetBadgeBrush(DisplayName, ProcessName);
    public string Tooltip => $"Tên: {DisplayName}\nTiến trình: {ProcessName}\n{PathText}";

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (Set(ref _enabled, value)) onChanged();
        }
    }

    private static string GetBadge(string name, string process)
    {
        if (name.Contains("LDPlayer", StringComparison.OrdinalIgnoreCase)) return "LD";
        if (name.Contains("BlueStacks", StringComparison.OrdinalIgnoreCase)) return "BS";
        if (name.Contains("Nox", StringComparison.OrdinalIgnoreCase)) return "Nx";
        if (name.Contains("MuMu", StringComparison.OrdinalIgnoreCase)) return "Mu";
        if (name.Contains("MEmu", StringComparison.OrdinalIgnoreCase)) return "ME";
        if (name.Contains("Minecraft", StringComparison.OrdinalIgnoreCase) || process.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase)) return "MC";
        if (name.Contains("TLauncher", StringComparison.OrdinalIgnoreCase)) return "TL";
        if (name.Contains("Roblox", StringComparison.OrdinalIgnoreCase)) return "Rb";
        if (name.Contains("Discord", StringComparison.OrdinalIgnoreCase)) return "Dc";
        if (name.Contains("Telegram", StringComparison.OrdinalIgnoreCase)) return "Tg";
        if (name.Contains("Steam", StringComparison.OrdinalIgnoreCase)) return "St";
        if (name.Contains("Epic", StringComparison.OrdinalIgnoreCase)) return "Eg";
        if (name.Contains("Garena", StringComparison.OrdinalIgnoreCase)) return "G";
        if (name.Contains("Riot", StringComparison.OrdinalIgnoreCase)) return "R";
        if (name.Contains("Liên Minh", StringComparison.OrdinalIgnoreCase)) return "LM";
        if (name.Contains("VALORANT", StringComparison.OrdinalIgnoreCase)) return "VL";
        if (name.Contains("Online", StringComparison.OrdinalIgnoreCase) || name.Contains("FIFA", StringComparison.OrdinalIgnoreCase)) return "FO";
        if (name.Contains("Đột Kích", StringComparison.OrdinalIgnoreCase)) return "CF";
        if (name.Contains("WARP", StringComparison.OrdinalIgnoreCase) || process.Contains("WARP", StringComparison.OrdinalIgnoreCase)) return "Wp";
        if (name.Contains("UltraSurf", StringComparison.OrdinalIgnoreCase)) return "Us";
        if (name.Contains("Proton", StringComparison.OrdinalIgnoreCase)) return "PV";
        var clean = name.Trim();
        return clean.Length >= 2 ? clean[..2].ToUpperInvariant() : clean.ToUpperInvariant();
    }

    private static Brush GetBadgeBrush(string name, string process)
    {
        string color = "#475569";
        if (name.Contains("LDPlayer", StringComparison.OrdinalIgnoreCase)) color = "#F59E0B";
        else if (name.Contains("BlueStacks", StringComparison.OrdinalIgnoreCase)) color = "#2563EB";
        else if (name.Contains("Nox", StringComparison.OrdinalIgnoreCase)) color = "#8B5CF6";
        else if (name.Contains("Minecraft", StringComparison.OrdinalIgnoreCase) || process.Equals("javaw.exe", StringComparison.OrdinalIgnoreCase)) color = "#16A34A";
        else if (name.Contains("TLauncher", StringComparison.OrdinalIgnoreCase)) color = "#059669";
        else if (name.Contains("Roblox", StringComparison.OrdinalIgnoreCase)) color = "#393B3D";
        else if (name.Contains("Discord", StringComparison.OrdinalIgnoreCase)) color = "#5865F2";
        else if (name.Contains("Telegram", StringComparison.OrdinalIgnoreCase)) color = "#29A9EB";
        else if (name.Contains("Steam", StringComparison.OrdinalIgnoreCase)) color = "#1B2838";
        else if (name.Contains("Epic", StringComparison.OrdinalIgnoreCase)) color = "#2F2F2F";
        else if (name.Contains("Garena", StringComparison.OrdinalIgnoreCase)) color = "#EE4D2D";
        else if (name.Contains("Riot", StringComparison.OrdinalIgnoreCase)) color = "#D13639";
        else if (name.Contains("Liên Minh", StringComparison.OrdinalIgnoreCase)) color = "#0A323D";
        else if (name.Contains("VALORANT", StringComparison.OrdinalIgnoreCase)) color = "#FA4454";
        else if (name.Contains("WARP", StringComparison.OrdinalIgnoreCase)) color = "#F6821F";
        else if (name.Contains("UltraSurf", StringComparison.OrdinalIgnoreCase)) color = "#DC2626";

        try { return (SolidColorBrush)new BrushConverter().ConvertFromString(color)!; }
        catch { return Brushes.DarkSlateGray; }
    }
}
