using System.Xml.Linq;
using CaoMinhComputer.Core.Models;
using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.Core.Tests;

public class AppLockerTests
{
    private const string Child = "S-1-5-21-1-2-3-1001";
    private static readonly AppLockerChild ChildWithProfile = new(Child, @"C:\Users\be");

    private const string ForeignPolicy = """
        <AppLockerPolicy Version="1">
          <RuleCollection Type="Exe" EnforcementMode="AuditOnly">
            <FilePathRule Id="11111111-1111-1111-1111-111111111111" Name="Org rule" Description="" UserOrGroupSid="S-1-5-32-544" Action="Allow">
              <Conditions><FilePathCondition Path="*" /></Conditions>
            </FilePathRule>
          </RuleCollection>
          <RuleCollection Type="Msi" EnforcementMode="Enabled" />
        </AppLockerPolicy>
        """;

    [Fact]
    public void Builds_child_scoped_deny_rules_only()
    {
        var rules = AppLockerManager.BuildRules([ChildWithProfile], ["Roblox", "explorer.exe"], strict: false, [], includeAllowAll: true);
        var allow = rules.Single(r => (string)r.Attribute("Action")! == "Allow");
        Assert.Equal("S-1-1-0", (string)allow.Attribute("UserOrGroupSid")!);
        var deny = rules.Where(r => (string)r.Attribute("Action")! == "Deny").ToList();
        Assert.Single(deny); // explorer.exe được bảo vệ, không bao giờ chặn
        Assert.All(deny, r => Assert.Equal(Child, (string)r.Attribute("UserOrGroupSid")!));
        Assert.Equal(@"*\Roblox.exe", (string)deny[0].Descendants("FilePathCondition").Single().Attribute("Path")!);
        Assert.All(rules, r => Assert.StartsWith(AppLockerManager.RulePrefix, (string)r.Attribute("Name")!));
    }

    [Fact]
    public void Strict_mode_denies_everything_except_windows_program_files_and_allowed_folders()
    {
        var rules = AppLockerManager.BuildRules([ChildWithProfile], [], strict: true,
            [@"%USERPROFILE%\AppData\Roaming\Zoom", @"D:\HocTap\", "C:\\"], includeAllowAll: false);
        var strict = Assert.Single(rules);
        Assert.Equal("Deny", (string)strict.Attribute("Action")!);
        Assert.Equal("*", (string)strict.Element("Conditions")!.Element("FilePathCondition")!.Attribute("Path")!);
        var exceptions = strict.Element("Exceptions")!.Elements().Select(e => (string)e.Attribute("Path")!).ToList();
        Assert.Equal(["%WINDIR%\\*", "%PROGRAMFILES%\\*", @"C:\Users\be\AppData\Roaming\Zoom\*", @"D:\HocTap\*"], exceptions);
    }

    [Theory]
    [InlineData(@"C:\", null)]
    [InlineData(@"D:", null)]
    [InlineData(@"%USERPROFILE%", null)]
    [InlineData(@"C:\Games\*", @"C:\Games\*")]
    [InlineData(@"C:\a*b", null)]
    [InlineData(@"%USERPROFILE%\Zoom", @"C:\Users\be\Zoom\*")]
    public void Normalizes_allowed_folders(string input, string? expected) =>
        Assert.Equal(expected, AppLockerManager.NormalizeFolder(input, @"C:\Users\be"));

    [Fact]
    public void Profile_variable_without_profile_is_skipped() =>
        Assert.Null(AppLockerManager.NormalizeFolder(@"%USERPROFILE%\Zoom", null));

    [Fact]
    public void Detects_foreign_group_policy_rules()
    {
        Assert.True(AppLockerManager.HasForeignExeRules(ForeignPolicy));
        Assert.False(AppLockerManager.HasForeignExeRules("<AppLockerPolicy Version=\"1\" />"));
        Assert.False(AppLockerManager.HasForeignExeRules(null));
    }

    [Fact]
    public void Builds_csp_rule_collection_and_counts_rules_even_when_xml_escaped()
    {
        var rules = AppLockerManager.BuildRules([ChildWithProfile], ["Game.exe"], strict: true, [], includeAllowAll: true);
        var collection = AppLockerManager.BuildRuleCollection(rules);
        var root = XElement.Parse(collection);
        Assert.Equal("RuleCollection", root.Name.LocalName);
        Assert.Equal("Exe", (string?)root.Attribute("Type"));
        Assert.Equal("Enabled", (string?)root.Attribute("EnforcementMode"));
        Assert.Equal(3, AppLockerManager.CountOurRules(collection));
        Assert.Equal(3, AppLockerManager.CountOurRules(System.Security.SecurityElement.Escape(collection)));
        Assert.Equal(0, AppLockerManager.CountOurRules(ForeignPolicy));
        Assert.Equal(0, AppLockerManager.CountOurRules("không phải xml"));
    }

    [Fact]
    public void Detects_whether_csp_still_holds_our_rules()
    {
        var rules = AppLockerManager.BuildRules([ChildWithProfile], ["Game.exe"], false, [], includeAllowAll: false);
        Assert.True(AppLockerManager.HasOurRules(AppLockerManager.BuildRuleCollection(rules)));
        Assert.True(AppLockerManager.HasOurRules(AppLockerManager.MdmExistsWithoutContent));
        Assert.False(AppLockerManager.HasOurRules(null));
        Assert.False(AppLockerManager.HasOurRules("<RuleCollection Type=\"Exe\" EnforcementMode=\"NotConfigured\" />"));
        // Nhiều mục CSP được gộp trong <Groups>: mục rỗng + mục có quy tắc → vẫn phải thấy quy tắc.
        var grouped = "<Groups><RuleCollection Type=\"Exe\" EnforcementMode=\"Enabled\" />" + AppLockerManager.BuildRuleCollection(rules) + "</Groups>";
        Assert.Equal(1, AppLockerManager.CountOurRules(grouped));
    }

    [Fact]
    public void Stable_guid_is_deterministic_and_unique()
    {
        Assert.Equal(AppLockerManager.StableGuid("a"), AppLockerManager.StableGuid("a"));
        Assert.NotEqual(AppLockerManager.StableGuid("a"), AppLockerManager.StableGuid("b"));
    }

    [Fact]
    public void Edition_support()
    {
        Assert.True(AppLockerManager.IsSupported(new WindowsEditionInfo { EditionId = "Enterprise", Build = 26100 }));
        Assert.True(AppLockerManager.IsSupported(new WindowsEditionInfo { EditionId = "Professional", Build = 22631 }));
        Assert.False(AppLockerManager.IsSupported(new WindowsEditionInfo { EditionId = "Core", Build = 19045 }));
    }
}
