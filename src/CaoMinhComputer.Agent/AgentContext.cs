using System.Drawing;
using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.Agent;

/// <summary>Biểu tượng khay hệ thống trong phiên của con.</summary>
internal sealed class AgentContext : ApplicationContext
{
    private const string Website = "https://caominhcomputer.com/";
    private const string Hotline = "0936.203.209";

    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _requestItem;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 30_000 };
    private int _notChildCount;
    private long _watchRequestId;
    private bool _busy;

    public AgentContext()
    {
        _statusItem = new ToolStripMenuItem("Đang kết nối...") { Enabled = false };
        _requestItem = new ToolStripMenuItem("Xin thêm thời gian...", null, (_, _) => ShowRequestForm());
        var menu = new ContextMenuStrip { Font = new Font("Segoe UI", 10f) };
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_requestItem);
        menu.Items.Add(new ToolStripMenuItem("Đổi người dùng (để bố mẹ đăng nhập)...", null, (_, _) =>
        {
            if (MessageBox.Show(
                    "Máy sẽ về màn hình đăng nhập để bố mẹ chọn tài khoản của mình.\nCác ứng dụng con đang mở vẫn được giữ nguyên.",
                    "Đổi người dùng", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK)
                CaoMinhComputer.Core.Services.WindowsSessions.SwitchUser();
        }));
        menu.Items.Add(new ToolStripMenuItem("Về Con Ngoan", null, (_, _) =>
            MessageBox.Show(
                "Máy tính này đang được phụ huynh quản lý bằng CaoMinhComputer.\n\n" +
                "Phần mềm chỉ đếm thời gian dùng máy và chặn các trang/ứng dụng theo cài đặt của phụ huynh; " +
                "không ghi phím bấm, không chụp màn hình, không đọc tin nhắn.\n\n" +
                $"Website: {Website}\nHotline: {Hotline}",
                "Con Ngoan", MessageBoxButtons.OK, MessageBoxIcon.Information)));

        _icon = new NotifyIcon
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Information,
            Text = "Con Ngoan",
            ContextMenuStrip = menu,
            Visible = false
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) _icon.ShowBalloonTip(5000, "Con Ngoan", _statusItem.Text ?? "", ToolTipIcon.Info);
        };

        _timer.Tick += async (_, _) => await TickAsync();
        _timer.Start();
        _ = TickAsync();
    }

    private async Task TickAsync()
    {
        if (_busy) return;
        _busy = true;
        try
        {
            var response = await AgentProtocol.SendAsync(
                new AgentProtocol.Request(AgentProtocol.CmdReport, IdleSeconds: IdleDetector.GetEffectiveIdleSeconds()),
                Program.ServicePath);
            if (response is null)
            {
                _statusItem.Text = "Chưa kết nối được dịch vụ Con Ngoan";
                return;
            }

            if (!response.IsChild)
            {
                // Tài khoản phụ huynh hoặc chưa được đánh dấu là tài khoản của con → không cần Agent.
                if (++_notChildCount >= 2) ExitThread();
                _icon.Visible = false;
                return;
            }
            _notChildCount = 0;
            _icon.Visible = true;
            UpdateStatus(response);
            WatchRequest(response);
        }
        finally
        {
            _busy = false;
        }
    }

    private void UpdateStatus(AgentProtocol.Response r)
    {
        string text;
        if (!r.Managed) text = "Không giới hạn thời gian";
        else if (!r.Allowed) text = r.Description ?? "Đã hết thời gian dùng máy";
        else if (r.RemainingSeconds is { } s) text = $"Còn {TimePolicyEngine.FormatDuration(s)} dùng máy hôm nay";
        else text = "Không giới hạn số phút hôm nay";
        if (r.InBreak) text += " · đang giờ giải lao";

        _statusItem.Text = text;
        var tip = "Con Ngoan – " + text;
        _icon.Text = tip.Length > 127 ? tip[..127] : tip;
        _requestItem.Enabled = r.Managed && r.RequestStatus != "pending";
        _requestItem.Text = r.RequestStatus == "pending" ? $"Đang chờ phụ huynh duyệt {r.RequestMinutes} phút..." : "Xin thêm thời gian...";
    }

    /// <summary>Báo cho con khi phụ huynh duyệt/từ chối yêu cầu.</summary>
    private void WatchRequest(AgentProtocol.Response r)
    {
        if (r.RequestStatus == "pending")
        {
            _watchRequestId = r.RequestId;
            return;
        }
        if (_watchRequestId == 0 || r.RequestId != _watchRequestId) return;
        _watchRequestId = 0;
        if (r.RequestStatus == "approved")
            _icon.ShowBalloonTip(10_000, "Đã được thêm giờ", $"Phụ huynh đã đồng ý thêm {r.RequestMinutes} phút.", ToolTipIcon.Info);
        else
            _icon.ShowBalloonTip(10_000, "Yêu cầu bị từ chối", "Phụ huynh chưa đồng ý thêm thời gian.", ToolTipIcon.Warning);
    }

    private async void ShowRequestForm()
    {
        using var form = new RequestForm();
        if (form.ShowDialog() != DialogResult.OK) return;
        var response = await AgentProtocol.SendAsync(
            new AgentProtocol.Request(AgentProtocol.CmdRequest, Minutes: form.Minutes, Reason: form.Reason), Program.ServicePath);
        var message = response is null
            ? "Chưa kết nối được dịch vụ Con Ngoan. Hãy thử lại sau."
            : response.Ok ? "Đã gửi yêu cầu tới bố mẹ (qua ứng dụng và Telegram). Con vui lòng chờ bố mẹ duyệt nhé!" : response.Error ?? "Không gửi được.";
        MessageBox.Show(message, "Con Ngoan", MessageBoxButtons.OK,
            response?.Ok == true ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        await TickAsync();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}

/// <summary>Hộp thoại xin thêm thời gian.</summary>
internal sealed class RequestForm : Form
{
    private readonly RadioButton _r15 = new() { Text = "15 phút", AutoSize = true, Checked = true };
    private readonly RadioButton _r30 = new() { Text = "30 phút", AutoSize = true };
    private readonly RadioButton _r60 = new() { Text = "60 phút", AutoSize = true };
    private readonly TextBox _reason = new() { Width = 360, MaxLength = 200, PlaceholderText = "Lý do (ví dụ: con cần làm nốt bài tập)" };

    public int Minutes => _r60.Checked ? 60 : _r30.Checked ? 30 : 15;
    public string Reason => _reason.Text.Trim();

    public RequestForm()
    {
        Text = "Xin thêm thời gian – Con Ngoan";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);
        TopMost = true;

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        layout.Controls.Add(new Label { Text = "Con muốn xin thêm bao nhiêu phút?", AutoSize = true, Font = new Font("Segoe UI", 11f, FontStyle.Bold) });
        var radios = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 8) };
        radios.Controls.AddRange([_r15, _r30, _r60]);
        layout.Controls.Add(radios);
        layout.Controls.Add(_reason);
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Width = 360, Margin = new Padding(0, 12, 0, 0) };
        var send = new Button { Text = "Gửi phụ huynh", DialogResult = DialogResult.OK, AutoSize = true, BackColor = Color.FromArgb(0xFB, 0xBA, 0x1F), FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Hủy", DialogResult = DialogResult.Cancel, AutoSize = true };
        buttons.Controls.AddRange([send, cancel]);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = send;
        CancelButton = cancel;
    }
}
