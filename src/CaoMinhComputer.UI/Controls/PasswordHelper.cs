using System.Windows;
using System.Windows.Controls;

namespace CaoMinhComputer.UI.Controls;

/// <summary>Theo dõi PasswordBox rỗng hay không để hiển thị chữ gợi ý (PasswordBox không hỗ trợ binding mật khẩu – có chủ đích).</summary>
public static class PasswordHelper
{
    public static readonly DependencyProperty WatchProperty = DependencyProperty.RegisterAttached(
        "Watch", typeof(bool), typeof(PasswordHelper), new PropertyMetadata(false, OnWatchChanged));

    public static readonly DependencyProperty IsEmptyProperty = DependencyProperty.RegisterAttached(
        "IsEmpty", typeof(bool), typeof(PasswordHelper), new PropertyMetadata(true));

    public static bool GetWatch(DependencyObject d) => (bool)d.GetValue(WatchProperty);
    public static void SetWatch(DependencyObject d, bool value) => d.SetValue(WatchProperty, value);
    public static bool GetIsEmpty(DependencyObject d) => (bool)d.GetValue(IsEmptyProperty);
    public static void SetIsEmpty(DependencyObject d, bool value) => d.SetValue(IsEmptyProperty, value);

    private static void OnWatchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not PasswordBox box) return;
        box.PasswordChanged -= OnPasswordChanged;
        if ((bool)e.NewValue)
        {
            box.PasswordChanged += OnPasswordChanged;
            SetIsEmpty(box, box.SecurePassword.Length == 0);
        }
    }

    private static void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        var box = (PasswordBox)sender;
        SetIsEmpty(box, box.SecurePassword.Length == 0);
    }
}
