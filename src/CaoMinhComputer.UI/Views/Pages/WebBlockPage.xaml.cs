using System.Windows;
using System.Windows.Controls;
using CaoMinhComputer.UI.ViewModels;
using Microsoft.Win32;

namespace CaoMinhComputer.UI.Views.Pages;

public partial class WebBlockPage : UserControl
{
    private readonly WebBlockViewModel _vm = new();

    public WebBlockPage()
    {
        InitializeComponent();
        DataContext = _vm;
    }

    private void OnPickExe(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Chọn file EXE của ứng dụng/game cần chặn",
            Filter = "Ứng dụng (*.exe)|*.exe",
            CheckFileExists = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            _vm.AddApp(null, dialog.FileName);
    }
}
