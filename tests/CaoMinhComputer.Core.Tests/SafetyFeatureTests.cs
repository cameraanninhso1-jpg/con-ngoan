using System.Text.Json.Nodes;
using CaoMinhComputer.Core.Services;
using Microsoft.Win32;

namespace CaoMinhComputer.Core.Tests;

/// <summary>Các tính năng 1.6.0: chính sách trình duyệt theo tài khoản con, xác thực Telegram, giám sát tiến trình.</summary>
public class SafetyFeatureTests
{
    [Fact]
    public void Browser_policies_only_target_user_hive_paths()
    {
        Assert.NotEmpty(BrowserPolicyService.Rules);
        foreach (var r in BrowserPolicyService.Rules)
        {
            Assert.StartsWith(@"Software\Policies\", r.Path);
            Assert.DoesNotContain("HKLM", BrowserPolicyService.StoreKey("S-1-5-21-1-2-3-1001", r), StringComparison.OrdinalIgnoreCase);
        }
        Assert.All(new[] { "Google\\Chrome", "Microsoft\\Edge", "BraveSoftware\\Brave", "CocCoc\\Browser", "Mozilla\\Firefox" },
            browser => Assert.Contains(BrowserPolicyService.Rules, r => r.Path.Contains(browser)));
    }

    [Fact]
    public void Options_none_applies_no_rule_and_each_option_maps_to_rules()
    {
        Assert.DoesNotContain(BrowserPolicyService.Rules, r => r.When(BrowserPolicyService.Options.None));
        var doh = new BrowserPolicyService.Options(true, false, false, false);
        Assert.All(BrowserPolicyService.Rules.Where(r => r.When(doh)), r => Assert.Contains("DNS", r.Name + r.Path, StringComparison.OrdinalIgnoreCase));
        var incognito = new BrowserPolicyService.Options(false, false, false, true);
        Assert.Contains(BrowserPolicyService.Rules, r => r.When(incognito) && r.Name == "InPrivateModeAvailability");
        Assert.Contains(BrowserPolicyService.Rules, r => r.When(incognito) && r.Name == "DisablePrivateBrowsing");
    }

    [Theory]
    [InlineData(RegistryValueKind.DWord, 1)]
    [InlineData(RegistryValueKind.String, "automatic")]
    [InlineData(RegistryValueKind.String, "123")] // chuỗi trông như số vẫn giữ kiểu chuỗi
    public void Original_registry_values_round_trip_with_their_type(RegistryValueKind kind, object value)
    {
        var restored = BrowserPolicyService.Deserialize(BrowserPolicyService.Serialize(kind, value));
        Assert.NotNull(restored);
        Assert.Equal(kind, restored!.Value.Kind);
        Assert.Equal(value.ToString(), restored.Value.Value.ToString());
    }

    private static JsonNode Chat(string type, string id) => new JsonObject { ["type"] = type, ["id"] = long.Parse(id) };
    private static JsonNode From(string id) => new JsonObject { ["id"] = long.Parse(id) };

    [Fact]
    public void Telegram_accepts_only_private_messages_from_parent()
    {
        Assert.True(TelegramBotClient.IsFromParent(Chat("private", "12345"), From("12345"), "12345"));
        Assert.False(TelegramBotClient.IsFromParent(Chat("private", "999"), From("999"), "12345"));      // người lạ
        Assert.False(TelegramBotClient.IsFromParent(Chat("group", "12345"), From("12345"), "12345"));    // tin trong nhóm
        Assert.False(TelegramBotClient.IsFromParent(Chat("group", "-100"), From("777"), "-100"));        // Chat ID là nhóm
        Assert.False(TelegramBotClient.IsFromParent(Chat("private", "12345"), From("12345"), ""));       // chưa cấu hình
        Assert.False(TelegramBotClient.IsFromParent(null, From("12345"), "12345"));
    }

    [Fact]
    public void Realtime_watcher_ignores_processes_not_owned_by_child()
    {
        var killed = new List<string>();
        using var watcher = new RealtimeProcessWatcher(
            () => new HashSet<string>(["dotnet", "testhost"], StringComparer.OrdinalIgnoreCase),
            () => new HashSet<string>(["S-1-5-21-0-0-0-9999"], StringComparer.OrdinalIgnoreCase),
            (name, _) => killed.Add(name));
        Assert.False(watcher.InspectAndEnforce(Environment.ProcessId, System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe"));
        Assert.Empty(killed);
    }
}
