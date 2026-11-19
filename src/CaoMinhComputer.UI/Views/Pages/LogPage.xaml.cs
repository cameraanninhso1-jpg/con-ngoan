using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Data.Repositories;
using CaoMinhComputer.UI.ViewModels;
using Microsoft.Win32;

namespace CaoMinhComputer.UI.Views.Pages;

public partial class LogPage : UserControl
{
    private const string AllCategories = "Tất cả";
    private readonly Data.Services.ProtectionCoordinator _c = AppServices.Coordinator;
    private readonly StatusMessage _status = new();
    private IReadOnlyList<LogEntry> _current = [];

    public LogPage()
    {
        InitializeComponent();
        Banner.DataContext = _status;
        CategoryCombo.ItemsSource = new[] { AllCategories }.Concat(LogCategory.All).ToList();
        CategoryCombo.SelectedIndex = 0;
        FromPicker.SelectedDate = DateTime.Today.AddDays(-30);
        ToPicker.SelectedDate = DateTime.Today;
        Loaded += (_, _) => Reload();
    }

    private LogQuery BuildQuery(int limit) => new()
    {
        FromLocal = FromPicker.SelectedDate,
        ToLocal = ToPicker.SelectedDate,
        Category = CategoryCombo.SelectedItem as string is { } cat && cat != AllCategories ? cat : null,
        Search = SearchBox.Text,
        Limit = limit
    };

    private void Reload()
    {
        if (FromPicker.SelectedDate > ToPicker.SelectedDate)
        {
            _status.Show("\"Từ ngày\" phải trước \"Đến ngày\".", true);
            return;
        }
        _current = _c.Logs.Query(BuildQuery(1000));
        LogGrid.ItemsSource = _current;
        CountText.Text = _current.Count >= 1000
            ? "Hiển thị 1.000 sự kiện mới nhất (thu hẹp bộ lọc để xem thêm)."
            : $"{_current.Count} sự kiện.";
    }

    private void OnFilter(object sender, RoutedEventArgs e)
    {
        _status.Clear();
        Reload();
    }

    private void OnSearchKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnFilter(sender, e);
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Xuất nhật ký",
            Filter = "CSV (*.csv)|*.csv",
            FileName = $"ConNgoan-nhatky-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            var entries = _c.Logs.Query(BuildQuery(LogRepository.MaxRows));
            LogRepository.ExportCsv(entries, dialog.FileName);
            _c.Logs.Info(LogCategory.Admin, $"Xuất {entries.Count} dòng nhật ký ra file CSV.");
            _status.Show($"Đã xuất {entries.Count} sự kiện ra {dialog.FileName}.", false);
        }
        catch (Exception ex)
        {
            _status.Show("Không xuất được file: " + ex.Message, true);
        }
    }

    private void OnClear(object sender, RoutedEventArgs e)
    {
        if (!DialogWindow.ReAuthenticate(Window.GetWindow(this), "xóa toàn bộ nhật ký hoạt động")) return;
        _c.Logs.Clear();
        _c.Logs.Warn(LogCategory.Admin, $"Quản trị \"{AppServices.CurrentAdmin}\" đã xóa toàn bộ nhật ký.");
        _status.Show("Đã xóa nhật ký. Hành động xóa được ghi lại.", false);
        Reload();
    }
}
