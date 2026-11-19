using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.Core.Tests;

/// <summary>Kiểm tra dữ liệu nhập khi tạo tài khoản Windows cho con ngay trong ứng dụng.</summary>
public class AccountTests
{
    [Theory]
    [InlineData("Bin")]
    [InlineData("MinhAnh")]
    [InlineData("be.na")]
    [InlineData("Minh Anh")]
    [InlineData("Nguyễn")]
    [InlineData("  Bin  ")]
    public void Accepts_valid_user_names(string name) => Assert.Null(LocalAccountService.ValidateNewUserName(name));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abcdefghijklmnopqrstu")] // 21 ký tự
    [InlineData("bin.")]
    [InlineData("...")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("con@nha")]
    [InlineData("a*b")]
    [InlineData("a\"b")]
    [InlineData("a[1]")]
    [InlineData("tab\tname")]
    public void Rejects_invalid_user_names(string? name) => Assert.NotNull(LocalAccountService.ValidateNewUserName(name));

    [Fact]
    public void Rejects_machine_name() => Assert.NotNull(LocalAccountService.ValidateNewUserName(Environment.MachineName));

    [Fact]
    public void Empty_password_is_allowed() => Assert.Null(LocalAccountService.ValidateNewPassword("", "", "Bin"));

    [Fact]
    public void Password_must_match_confirmation() =>
        Assert.Contains("không khớp", LocalAccountService.ValidateNewPassword("Abc12345", "Abc12346", "Bin"));

    [Fact]
    public void Password_must_not_contain_user_name() =>
        Assert.NotNull(LocalAccountService.ValidateNewPassword("minhanh2015", "minhanh2015", "MinhAnh"));

    [Fact]
    public void Normal_password_is_accepted() => Assert.Null(LocalAccountService.ValidateNewPassword("Hoc-Gioi-2025", "Hoc-Gioi-2025", "Bin"));
}
