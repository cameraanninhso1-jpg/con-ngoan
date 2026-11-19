using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.UI.ViewModels;

namespace CaoMinhComputer.UI.Views.Pages;

public sealed class AccountItem(LocalAccountInfo info, bool isChild, Action onChanged) : ObservableObject
{
    private static readonly string[] Palette = ["#4573E3", "#F5A400", "#1D9A57", "#D6246E", "#7B5BD6", "#0F9DB0"];
    private bool _isChild = isChild;

    public LocalAccountInfo Info { get; } = info;
    public string Name => Info.Name;
    public string Initial => Info.Name.Length > 0 ? Info.Name[..1].ToUpperInvariant() : "?";
    public Brush AvatarBrush => (Brush)new BrushConverter().ConvertFromString(Palette[Info.Sid.Sum(ch => ch) % Palette.Length])!;

    public string Tags
    {
        get
        {
            var tags = new List<string>();
            if (Info.IsCurrentUser) tags.Add("bạn đang dùng");
            if (Info.IsDisabled) tags.Add("đã vô hiệu hóa");
            if (!Info.HasProfile) tags.Add("chưa đăng nhập lần nào");
            return tags.Count == 0 ? "" : "  (" + string.Join(", ", tags) + ")";
        }
    }

    public string TypeText => Info.IsAdministrator ? "Administrator (quản trị viên)" : "Standard User (người dùng chuẩn)";
    public Brush TypeBrush => Info.IsAdministrator
        ? (Brush)Application.Current.FindResource("WarningBrush")
        : (Brush)Application.Current.FindResource("SuccessBrush");

    public bool CanBeChild => !Info.IsCurrentUser;
    public bool CanChangeType => !Info.IsCurrentUser;
    public string ActionText => Info.IsAdministrator ? "Chuyển thành Standard User" : "Cấp lại quyền quản trị";

    public bool IsChild
    {
        get => _isChild;
        set
        {
            if (Set(ref _isChild, value)) onChanged();
        }
    }
}

public partial class InstallLockPage : UserControl
{
    private readonly Data.Services.ProtectionCoordinator _c = AppServices.Coordinator;
    private readonly StatusMessage _status = new();
    private List<AccountItem> _accounts = [];
    private bool _loading;

    public InstallLockPage()
    {
        InitializeComponent();
        Banner.DataContext = _status;
        Loaded += (_, _) => Load();
    }

    private void Load()
    {
        _loading = true;
        try
        {
            var edition = WindowsEdition.Detect();
            EditionText.Text = edition.FriendlyName;
            EditionSupportText.Text = edition.AppControlSupport;

            var childSids = _c.Settings.GetChildSids().ToHashSet(StringComparer.OrdinalIgnoreCase);
            _accounts = LocalAccountService.GetAccounts()
                .Select(a => new AccountItem(a, childSids.Contains(a.Sid), MarkDirty))
                .ToList();
            AccountsList.ItemsSource = _accounts;
            NoAccountsText.Visibility = _accounts.Any(a => !a.Info.IsCurrentUser) ? Visibility.Collapsed : Visibility.Visible;

            var policy = _c.InstallPolicy;
            UacOffWarning.Visibility = policy.IsUacEnabled() ? Visibility.Collapsed : Visibility.Visible;
            if (policy.GetConsentPromptBehaviorUser() == InstallPolicyService.ConsentAutoDeny) RadioDeny.IsChecked = true;
            else RadioPrompt.IsChecked = true;

            MsiCheck.IsChecked = policy.IsUserInstallsDisabled();
            MsiForeignNote.Visibility = MsiCheck.IsChecked == true && !policy.IsUserInstallsManagedByUs()
                ? Visibility.Visible
                : Visibility.Collapsed;

            LoadAppLocker(edition);
        }
        catch (Exception ex)
        {
            _status.Show("Không đọc được thông tin tài khoản/chính sách Windows: " + ex.Message, true);
        }
        finally
        {
            _loading = false;
            DirtyPill.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadAppLocker(WindowsEditionInfo edition)
    {
        var available = _c.AppLockerAvailable;
        StrictCheck.IsChecked = _c.StrictMode;
        AllowedFoldersBox.Text = string.Join(Environment.NewLine, _c.AllowedFolders);
        StrictCheck.IsEnabled = AllowedFoldersBox.IsEnabled = available;
        if (!available)
        {
            AppLockerTitle.Text = "AppLocker không khả dụng trên máy này";
            AppLockerDetail.Text = AppLockerManager.SupportText(edition);
            AppLockerBadgeBg.Background = (System.Windows.Media.Brush)FindResource("DangerSoftBrush");
            AppLockerBadge.Foreground = (System.Windows.Media.Brush)FindResource("DangerBrush");
            return;
        }
        var running = AppLockerManager.IsAppIdServiceRunning();
        AppLockerTitle.Text = _c.StrictMode
            ? "AppLocker: chế độ nghiêm ngặt đang bật"
            : "AppLocker sẵn sàng";
        AppLockerDetail.Text = AppLockerManager.SupportText(edition) +
                               (running ? " Dịch vụ Application Identity đang chạy." : " Dịch vụ Application Identity sẽ được bật khi áp dụng.");
    }

    private void OnStrictChanged(object sender, RoutedEventArgs e) => MarkDirty();

    private void OnFoldersChanged(object sender, TextChangedEventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        if (!_loading) DirtyPill.Visibility = Visibility.Visible;
    }

    private void OnChanged(object sender, RoutedEventArgs e) => MarkDirty();

    private void OnAccountAction(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not AccountItem item) return;
        var window = Window.GetWindow(this);
        ApplyResult result;
        try
        {
            if (item.Info.IsAdministrator)
            {
                if (!DialogWindow.Confirm(window, "Chuyển thành Standard User",
                        $"Tài khoản \"{item.Name}\" sẽ không còn quyền Administrator. Khi cần cài phần mềm, Windows sẽ hỏi mật khẩu của tài khoản quản trị khác.\n\nHãy chắc chắn bạn nhớ mật khẩu Windows của tài khoản phụ huynh.",
                        "Chuyển đổi"))
                    return;
                result = LocalAccountService.MakeStandardUser(item.Info.Sid);
            }
            else
            {
                if (!DialogWindow.ReAuthenticate(window, $"cấp quyền Administrator cho \"{item.Name}\"")) return;
                result = LocalAccountService.MakeAdministrator(item.Info.Sid);
            }
        }
        catch (Exception ex)
        {
            result = ApplyResult.Fail("Không thay đổi được loại tài khoản: " + ex.Message);
        }

        _c.Logs.Add(result.Success ? LogLevel.Info : LogLevel.Error, LogCategory.Install, result.Message);
        var dirty = DirtyPill.Visibility;
        Load();
        DirtyPill.Visibility = dirty;
        _status.Show(result);
    }

    private async void OnCreateChild(object sender, RoutedEventArgs e)
    {
        var window = Window.GetWindow(this);
        var dialog = CreateChildDialog.Run(window);
        if (dialog?.CreatedSid is not { } sid) return;

        _c.Logs.Info(LogCategory.Install, dialog.Result!.Message + " Đã đánh dấu là tài khoản của con.");
        CreateChildButton.IsEnabled = false;
        string applyMessage;
        try
        {
            // Lưu ngay vào danh sách tài khoản con (giữ nguyên các tài khoản con đã lưu trước đó) và áp dụng chặn.
            var applied = await Task.Run(() =>
            {
                var children = _c.Settings.GetChildSids().Append(sid).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                _c.Settings.SetChildSids(children);
                return _c.ApplyAppBlocking();
            });
            applyMessage = applied.Message;
            await AppServices.SendToServiceAsync(PipeProtocol.CmdRefresh);
        }
        catch (Exception ex)
        {
            applyMessage = "Chưa áp dụng được chặn ứng dụng: " + ex.Message;
        }
        finally
        {
            CreateChildButton.IsEnabled = true;
        }

        var dirty = DirtyPill.Visibility;
        Load();
        DirtyPill.Visibility = dirty;
        _status.Show($"{dialog.Result!.Message} Đã đánh dấu là tài khoản của con.\n{applyMessage}", false);

        AppServices.SwitchWindowsUser(window,
            $"Tài khoản \"{dialog.CreatedName}\" đã sẵn sàng. Nên đăng nhập vào tài khoản này một lần ngay bây giờ để Windows tạo hồ sơ – " +
            "Con Ngoan sẽ tự áp dụng chặn ứng dụng khi con đăng nhập lần đầu. Đổi người dùng ngay?");
    }

    private void OnSwitchUser(object sender, RoutedEventArgs e) => AppServices.SwitchWindowsUser(Window.GetWindow(this));

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        SaveButton.Content = "Đang áp dụng...";
        var children = _accounts.Where(a => a.IsChild).Select(a => a.Info).ToList();
        var deny = RadioDeny.IsChecked == true;
        var msi = MsiCheck.IsChecked == true;
        var strict = StrictCheck.IsChecked == true && _c.AppLockerAvailable;
        var folderLines = AllowedFoldersBox.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var messages = new List<string>();
        var failed = false;

        var badFolders = folderLines.Where(f => AppLockerManager.NormalizeFolder(f, @"C:\Users\x") is null).ToList();
        if (badFolders.Count > 0)
        {
            _status.Show("Thư mục được phép không hợp lệ hoặc quá rộng (không được là ổ đĩa gốc hay cả thư mục người dùng): " + string.Join(", ", badFolders), true);
            SaveButton.IsEnabled = true;
            SaveButton.Content = "Lưu & áp dụng";
            return;
        }
        if (strict && children.Count == 0)
            messages.Add("⚠ Chưa đánh dấu tài khoản của con nên chế độ nghiêm ngặt chưa áp dụng cho ai.");

        try
        {
            var appResult = await Task.Run(() =>
            {
                _c.Settings.SetChildSids(children.Select(c => c.Sid));
                _c.Settings.SetBool(SettingKeys.AppLockerStrict, strict);
                _c.Settings.Set(SettingKeys.AppLockerAllowedFolders, string.Join("\n", folderLines));
                _c.InstallPolicy.SetConsentPromptBehaviorUser(deny ? InstallPolicyService.ConsentAutoDeny : InstallPolicyService.ConsentPromptCredentials);
                _c.InstallPolicy.SetUserInstallsDisabled(msi);
                _c.Logs.Info(LogCategory.Install,
                    $"Lưu khóa cài đặt: tài khoản con = {string.Join(", ", children.Select(c => c.Name).DefaultIfEmpty("(chưa chọn)"))}; " +
                    $"UAC với Standard User = {(deny ? "tự động từ chối" : "yêu cầu mật khẩu quản trị")}; chặn MSI theo người dùng = {(msi ? "bật" : "tắt")}; " +
                    $"chế độ nghiêm ngặt (AppLocker) = {(strict ? "bật" : "tắt")}; thư mục được phép: {string.Join(", ", folderLines.DefaultIfEmpty("(không)"))}.");
                return _c.ApplyAppBlocking();
            });

            // Kiểm chứng lại bằng cách đọc registry.
            var actualDeny = _c.InstallPolicy.GetConsentPromptBehaviorUser() == InstallPolicyService.ConsentAutoDeny;
            if (actualDeny != deny)
            {
                failed = true;
                messages.Add("Không ghi được thiết lập UAC (có thể bị Group Policy của tổ chức ghi đè).");
            }
            else
            {
                messages.Add(deny
                    ? "UAC: yêu cầu nâng quyền từ Standard User sẽ bị tự động từ chối."
                    : "UAC: Standard User phải nhập tài khoản + mật khẩu quản trị khi cài phần mềm.");
            }
            messages.Add(_c.InstallPolicy.IsUserInstallsDisabled() ? "Đã chặn cài MSI theo từng người dùng." : "Không chặn cài MSI theo người dùng.");

            var admins = children.Where(c => c.IsAdministrator).Select(c => c.Name).ToList();
            if (admins.Count > 0)
            {
                failed = true;
                messages.Add($"⚠ Tài khoản của con vẫn là Administrator: {string.Join(", ", admins)}. Hãy bấm \"Chuyển thành Standard User\" – nếu không, con có thể tự gỡ mọi chặn.");
            }
            messages.Add("Ứng dụng: " + appResult.Message);
            failed |= !appResult.Success && children.Count > 0;
            _status.Show(string.Join("\n", messages), failed);
            LoadAppLocker(WindowsEdition.Detect());
            if (!failed) DirtyPill.Visibility = Visibility.Collapsed;
            _ = AppServices.SendToServiceAsync(PipeProtocol.CmdRefresh); // dịch vụ áp dụng ngay phạm vi chặn website theo tài khoản con
        }
        catch (Exception ex)
        {
            _c.Logs.Error(LogCategory.Install, "Lỗi áp dụng khóa cài đặt: " + ex.Message);
            _status.Show("Lỗi áp dụng: " + ex.Message, true);
        }
        finally
        {
            SaveButton.IsEnabled = true;
            SaveButton.Content = "Lưu & áp dụng";
        }
    }
}
