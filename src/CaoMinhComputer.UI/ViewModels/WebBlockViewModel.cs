using System.Collections.ObjectModel;
using System.Windows.Input;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Repositories;

namespace CaoMinhComputer.UI.ViewModels;

public sealed class WebBlockViewModel : ObservableObject
{
    private readonly Data.Services.ProtectionCoordinator _c = AppServices.Coordinator;
    private bool _isDirty;
    private bool _isBusy;
    private bool _loading;
    private string _customDomainsText = "";
    private string? _domainNotes;
    private string _newProcessText = "";
    private bool _autoClose;
    private bool _disableDoH = true;
    private bool _blockExtensions;
    private bool _safeSearch = true;
    private bool _blockIncognito = true;

    public ObservableCollection<PlatformItemViewModel> Social { get; } = [];
    public ObservableCollection<PlatformItemViewModel> Games { get; } = [];
    public ObservableCollection<PlatformItemViewModel> WebGames { get; } = [];
    public ObservableCollection<PlatformItemViewModel> BypassTools { get; } = [];
    public ObservableCollection<PlatformItemViewModel> Entertainment { get; } = [];
    public List<PlatformItemViewModel> AllPlatforms { get; } = [];
    public ObservableCollection<AppRuleViewModel> Apps { get; } = [];
    public StatusMessage Status { get; } = new();

    public ICommand ClearAllCommand { get; }
    public ICommand ToggleSocialCommand { get; }
    public ICommand ToggleGamesCommand { get; }
    public ICommand ToggleWebGamesCommand { get; }
    public ICommand ToggleBypassToolsCommand { get; }
    public ICommand ToggleEntertainmentCommand { get; }
    public ICommand ToggleAppsCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand AddProcessCommand { get; }
    public ICommand RemoveAppCommand { get; }

    public WebBlockViewModel()
    {
        foreach (var p in PlatformCatalog.All)
        {
            var vm = new PlatformItemViewModel(p, OnPlatformChanged);
            AllPlatforms.Add(vm);
            if (p.Groups.Contains(PlatformGroup.Social)) Social.Add(vm);
            if (p.Groups.Contains(PlatformGroup.Game)) Games.Add(vm);
            if (p.Groups.Contains(PlatformGroup.WebGame)) WebGames.Add(vm);
            if (p.Groups.Contains(PlatformGroup.BypassTools)) BypassTools.Add(vm);
            if (p.Groups.Contains(PlatformGroup.Entertainment)) Entertainment.Add(vm);
        }

        ClearAllCommand = new RelayCommand(() => SetAll(AllPlatforms, false));
        ToggleSocialCommand = new RelayCommand(() => SetAll(Social, !Social.All(p => p.IsBlocked)));
        ToggleGamesCommand = new RelayCommand(() => SetAll(Games, !Games.All(p => p.IsBlocked)));
        ToggleWebGamesCommand = new RelayCommand(() => SetAll(WebGames, !WebGames.All(p => p.IsBlocked)));
        ToggleBypassToolsCommand = new RelayCommand(() => SetAll(BypassTools, !BypassTools.All(p => p.IsBlocked)));
        ToggleEntertainmentCommand = new RelayCommand(() => SetAll(Entertainment, !Entertainment.All(p => p.IsBlocked)));
        ToggleAppsCommand = new RelayCommand(() =>
        {
            var target = !Apps.All(a => a.Enabled);
            foreach (var a in Apps) a.Enabled = target;
            OnPropertyChanged(nameof(AppsToggleText));
            if (!_loading) IsDirty = true;
        });
        SaveCommand = new RelayCommand(async _ => await SaveAsync(), _ => !IsBusy);
        AddProcessCommand = new RelayCommand(() => AddApp(NewProcessText, null));
        RemoveAppCommand = new RelayCommand(p => RemoveApp(p as AppRuleViewModel));
        Load();
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => Set(ref _isDirty, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value)) OnPropertyChanged(nameof(SaveButtonText));
        }
    }

    public string SaveButtonText => IsBusy ? "Đang áp dụng..." : "Lưu & áp dụng";

    public int SelectedCount => AllPlatforms.Count(p => p.IsBlocked);
    public string SelectedText => $"Đang chọn {SelectedCount}/{AllPlatforms.Count} nền tảng";
    public string SocialToggleText => Social.All(p => p.IsBlocked) ? "Bỏ chọn nhóm" : "Chọn cả nhóm";
    public string GamesToggleText => Games.All(p => p.IsBlocked) ? "Bỏ chọn nhóm" : "Chọn cả nhóm";
    public string WebGamesToggleText => WebGames.All(p => p.IsBlocked) ? "Bỏ chọn nhóm" : "Chọn cả nhóm";
    public string BypassToolsToggleText => BypassTools.All(p => p.IsBlocked) ? "Bỏ chọn nhóm" : "Chọn cả nhóm";
    public string EntertainmentToggleText => Entertainment.All(p => p.IsBlocked) ? "Bỏ chọn nhóm" : "Chọn cả nhóm";
    public string AppsToggleText => Apps.Count > 0 && Apps.All(a => a.Enabled) ? "Bỏ chọn nhóm" : "Chọn cả nhóm";

    public bool BlockAll
    {
        get => AllPlatforms.All(p => p.IsBlocked);
        set => SetAll(AllPlatforms, value);
    }

    public string CustomDomainsText
    {
        get => _customDomainsText;
        set
        {
            if (Set(ref _customDomainsText, value) && !_loading) IsDirty = true;
        }
    }

    public string? DomainNotes
    {
        get => _domainNotes;
        private set
        {
            if (Set(ref _domainNotes, value)) OnPropertyChanged(nameof(HasDomainNotes));
        }
    }

    public bool HasDomainNotes => !string.IsNullOrEmpty(DomainNotes);

    public string NewProcessText
    {
        get => _newProcessText;
        set => Set(ref _newProcessText, value);
    }

    public bool AutoClose
    {
        get => _autoClose;
        set
        {
            if (Set(ref _autoClose, value) && !_loading) IsDirty = true;
        }
    }

    public bool DisableDoH
    {
        get => _disableDoH;
        set
        {
            if (Set(ref _disableDoH, value) && !_loading) IsDirty = true;
        }
    }

    public bool BlockExtensions
    {
        get => _blockExtensions;
        set
        {
            if (Set(ref _blockExtensions, value) && !_loading) IsDirty = true;
        }
    }

    public bool SafeSearch
    {
        get => _safeSearch;
        set
        {
            if (Set(ref _safeSearch, value) && !_loading) IsDirty = true;
        }
    }

    public bool BlockIncognito
    {
        get => _blockIncognito;
        set
        {
            if (Set(ref _blockIncognito, value) && !_loading) IsDirty = true;
        }
    }

    public string ChildAccountsText
    {
        get
        {
            var sids = _c.Settings.GetChildSids();
            if (sids.Count == 0)
                return "⚠ Chưa chọn tài khoản Windows của con. Hãy chọn ở mục \"Khóa cài đặt\" để chặn ứng dụng có hiệu lực.";
            var names = sids.Select(s =>
            {
                try { return new System.Security.Principal.SecurityIdentifier(s).Translate(typeof(System.Security.Principal.NTAccount)).Value; }
                catch (Exception) { return s; }
            });
            return "Áp dụng cho tài khoản của con: " + string.Join(", ", names);
        }
    }

    public bool IsPaused => _c.IsPaused;

    private bool _scopeChildren = true;

    /// <summary>Chỉ chặn website khi tài khoản của con đang dùng máy.</summary>
    public bool ScopeChildren
    {
        get => _scopeChildren;
        set
        {
            if (!Set(ref _scopeChildren, value)) return;
            OnPropertyChanged(nameof(ScopeAll));
            if (!_loading) IsDirty = true;
        }
    }

    public bool ScopeAll
    {
        get => !_scopeChildren;
        set => ScopeChildren = !value;
    }

    /// <summary>Trạng thái hiện tại: có đang chặn website hay không và vì sao.</summary>
    public string ScopeStateText
    {
        get
        {
            var d = _c.EvaluateWebScope();
            if (_c.IsPaused) return "Hiện tại: bảo vệ đang tạm tắt – không chặn website.";
            if (_c.IsInBreakNow()) return "Hiện tại: đang trong giờ giải lao – tạm mở chặn.";
            return d.Block ? $"Hiện tại: đang chặn website ({d.Reason})." : $"Hiện tại: chưa chặn website vì {d.Reason}.";
        }
    }

    private void Load()
    {
        _loading = true;
        var states = _c.Blocks.GetPlatformStates();
        foreach (var p in AllPlatforms) p.Load(states.TryGetValue(p.Key, out var on) && on);
        CustomDomainsText = string.Join(Environment.NewLine, _c.Blocks.GetCustomDomains());
        AutoClose = _c.Settings.GetBool(SettingKeys.AppAutoClose);
        DisableDoH = _c.Settings.GetBool(SettingKeys.BrowserDisableDoH, fallback: true);
        BlockExtensions = _c.Settings.GetBool(SettingKeys.BrowserBlockExtensions, fallback: false);
        SafeSearch = _c.Settings.GetBool(SettingKeys.BrowserSafeSearch, fallback: true);
        BlockIncognito = _c.Settings.GetBool(SettingKeys.BrowserBlockIncognito, fallback: true);
        ScopeChildren = _c.WebScope == WebBlockScope.Children;
        ReloadApps();
        _loading = false;
        RaiseCounts();
        IsDirty = false;
    }

    private void ReloadApps()
    {
        Apps.Clear();
        foreach (var rule in _c.Apps.GetAll())
            Apps.Add(new AppRuleViewModel(rule, () =>
            {
                if (!_loading) IsDirty = true;
                OnPropertyChanged(nameof(AppsToggleText));
            }));
        OnPropertyChanged(nameof(AppsToggleText));
    }

    private void OnPlatformChanged()
    {
        if (_loading) return;
        IsDirty = true;
        RaiseCounts();
    }

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(SelectedText));
        OnPropertyChanged(nameof(BlockAll));
        OnPropertyChanged(nameof(SocialToggleText));
        OnPropertyChanged(nameof(GamesToggleText));
        OnPropertyChanged(nameof(WebGamesToggleText));
        OnPropertyChanged(nameof(BypassToolsToggleText));
        OnPropertyChanged(nameof(EntertainmentToggleText));
        OnPropertyChanged(nameof(AppsToggleText));
    }

    private static void SetAll(IEnumerable<PlatformItemViewModel> items, bool value)
    {
        foreach (var p in items) p.IsBlocked = value;
    }

    /// <summary>Thêm ứng dụng theo tên tiến trình hoặc đường dẫn file EXE.</summary>
    public void AddApp(string? input, string? path)
    {
        var exe = ProtectedProcesses.NormalizeExeName(path ?? input);
        if (exe is null)
        {
            Status.Show("Tên tiến trình không hợp lệ. Ví dụ: Discord.exe", true);
            return;
        }
        if (ProtectedProcesses.IsProtected(exe))
        {
            Status.Show($"{exe} là thành phần hệ thống/bảo mật/trợ năng, không được phép chặn.", true);
            return;
        }

        string display = Path.GetFileNameWithoutExtension(exe);
        if (path is not null)
        {
            try
            {
                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                if (!string.IsNullOrWhiteSpace(info.FileDescription)) display = info.FileDescription.Trim();
                else if (!string.IsNullOrWhiteSpace(info.ProductName)) display = info.ProductName.Trim();
            }
            catch (Exception)
            {
                // Không đọc được thông tin phiên bản – dùng tên file.
            }
        }

        if (!_c.Apps.Add(display, exe, path, enabled: true))
        {
            Status.Show($"{exe} đã có trong danh sách.", true);
            return;
        }
        _c.Logs.Info(LogCategory.Settings, $"Thêm ứng dụng vào danh sách chặn: {display} ({exe}){(path is null ? "" : " - " + path)}.");
        NewProcessText = "";
        ReloadApps();
        IsDirty = true;
        Status.Show($"Đã thêm {exe}. Bấm \"Lưu & áp dụng\" để có hiệu lực.", false);
    }

    private void RemoveApp(AppRuleViewModel? vm)
    {
        if (vm is null) return;
        _c.Apps.Delete(vm.Id);
        _c.Logs.Info(LogCategory.Settings, $"Xóa ứng dụng khỏi danh sách chặn: {vm.DisplayName} ({vm.ProcessName}).");
        Apps.Remove(vm);
        IsDirty = true;
        Status.Show($"Đã xóa {vm.ProcessName}. Bấm \"Lưu & áp dụng\" để gỡ chặn trên máy.", false);
    }

    public async Task SaveAsync()
    {
        // 1) Chuẩn hóa danh sách tên miền tùy chỉnh.
        var domains = new List<string>();
        var notes = new List<string>();
        var invalid = new List<string>();
        foreach (var line in CustomDomainsText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
        {
            if (DomainNormalizer.TryNormalize(line, out var d, out var note))
            {
                if (!domains.Contains(d)) domains.Add(d);
                if (note is not null) notes.Add($"{line}: {note}");
            }
            else
            {
                invalid.Add(note is null ? line : $"{line} ({note})");
            }
        }
        if (invalid.Count > 0)
        {
            Status.Show("Tên miền không hợp lệ, chưa lưu: " + string.Join(", ", invalid), true);
            return;
        }

        IsBusy = true;
        Status.Clear();
        try
        {
            var states = AllPlatforms.ToDictionary(p => p.Key, p => p.IsBlocked);
            var appStates = Apps.Select(a => (a.Id, a.Enabled)).ToList();
            var autoClose = AutoClose;
            var disableDoH = DisableDoH;
            var blockExt = BlockExtensions;
            var safeSearch = SafeSearch;
            var blockIncognito = BlockIncognito;
            var scopeChildren = ScopeChildren;

            var (web, app) = await Task.Run(() =>
            {
                _c.Blocks.Save(states, domains);
                foreach (var (id, enabled) in appStates) _c.Apps.SetEnabled(id, enabled);
                _c.Settings.SetBool(SettingKeys.AppAutoClose, autoClose);
                _c.Settings.SetBool(SettingKeys.BrowserDisableDoH, disableDoH);
                _c.Settings.SetBool(SettingKeys.BrowserBlockExtensions, blockExt);
                _c.Settings.SetBool(SettingKeys.BrowserSafeSearch, safeSearch);
                _c.Settings.SetBool(SettingKeys.BrowserBlockIncognito, blockIncognito);
                _c.Settings.Set(SettingKeys.WebScope, scopeChildren ? "children" : "all");
                _c.Logs.Info(LogCategory.Settings,
                    $"Lưu cấu hình chặn: {states.Count(s => s.Value)}/{states.Count} nền tảng, {domains.Count} tên miền tùy chỉnh, " +
                    $"{appStates.Count(a => a.Enabled)} ứng dụng, tự đóng: {(autoClose ? "bật" : "tắt")}, khóa DoH: {(disableDoH ? "bật" : "tắt")}, chặn extension: {(blockExt ? "bật" : "tắt")}, SafeSearch: {(safeSearch ? "bật" : "tắt")}, khóa ẩn danh: {(blockIncognito ? "bật" : "tắt")}.");
                return (_c.ApplyWebBlocking(), _c.ApplyAppBlocking());
            });

            _loading = true;
            CustomDomainsText = string.Join(Environment.NewLine, domains);
            _loading = false;
            DomainNotes = notes.Count > 0 ? string.Join(Environment.NewLine, notes) : null;

            var message = $"Website: {web.Message}\nỨng dụng: {app.Message}";
            if (_c.IsPaused) message = "Bảo vệ đang TẠM TẮT – cấu hình đã lưu, sẽ có hiệu lực khi bật lại bảo vệ.\n" + message;
            Status.Show(message, !web.Success || !app.Success);
            if (web.Success && app.Success) IsDirty = false;
        }
        catch (Exception ex)
        {
            _c.Logs.Error(LogCategory.Settings, "Lỗi khi lưu cấu hình chặn: " + ex.Message);
            Status.Show("Lỗi khi lưu/áp dụng: " + ex.Message, true);
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(ChildAccountsText));
            OnPropertyChanged(nameof(ScopeStateText));
        }
    }
}
