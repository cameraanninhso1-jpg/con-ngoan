using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.Core.Tests;

public class DomainNormalizerTests
{
    [Theory]
    [InlineData("shopee.vn", "shopee.vn")]
    [InlineData("  HTTPS://WWW.Shopee.VN/abc?x=1 ", "www.shopee.vn")]
    [InlineData("http://user:pass@example.com:8080/", "example.com")]
    [InlineData("*.example.org", "example.org")]
    [InlineData("thomaytinh.com/blog", "thomaytinh.com")]
    [InlineData("bücher.de", "xn--bcher-kva.de")]
    public void Normalizes_valid_input(string input, string expected)
    {
        Assert.True(DomainNormalizer.TryNormalize(input, out var d, out _));
        Assert.Equal(expected, d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("localhost")]
    [InlineData("192.168.1.1")]
    [InlineData("not a domain")]
    [InlineData("abc")]
    [InlineData("example.123")]
    [InlineData("-bad-.com")]
    public void Rejects_invalid_input(string input) =>
        Assert.False(DomainNormalizer.TryNormalize(input, out _, out _));

    [Fact]
    public void Path_produces_note()
    {
        Assert.True(DomainNormalizer.TryNormalize("thomaytinh.com/blog", out _, out var note));
        Assert.Contains("/blog", note);
    }

    [Fact]
    public void Expands_www_and_mobile_variants()
    {
        Assert.Equal(["shopee.vn", "www.shopee.vn", "m.shopee.vn"], DomainNormalizer.ExpandVariants("shopee.vn").ToArray());
        Assert.Equal(["www.shopee.vn", "shopee.vn"], DomainNormalizer.ExpandVariants("www.shopee.vn").ToArray());
    }
}

public class HostsContentTests
{
    private const string Foreign =
        "# Copyright (c) 1993-2009 Microsoft Corp.\r\n" +
        "127.0.0.1 localhost\r\n" +
        "# Phần mềm khác\r\n" +
        "10.0.0.5 intranet.local\r\n";

    [Fact]
    public void Adds_block_and_preserves_foreign_content()
    {
        var result = HostsBlockingProvider.BuildContent(Foreign, ["facebook.com", "www.facebook.com"], DateTime.Now);
        Assert.StartsWith(Foreign, result);
        Assert.Contains(HostsBlockingProvider.BeginMarker, result);
        Assert.Contains("0.0.0.0 facebook.com\r\n", result);
        Assert.Contains(":: www.facebook.com\r\n", result);
        Assert.Equal(["facebook.com", "www.facebook.com"], HostsBlockingProvider.ParseManagedDomains(result));
    }

    [Fact]
    public void Reapply_replaces_block_without_duplicating()
    {
        var first = HostsBlockingProvider.BuildContent(Foreign, ["a.com"], DateTime.Now);
        var second = HostsBlockingProvider.BuildContent(first, ["b.com"], DateTime.Now);
        Assert.Equal(1, CountOccurrences(second, HostsBlockingProvider.BeginMarker));
        Assert.Equal(["b.com"], HostsBlockingProvider.ParseManagedDomains(second));
        Assert.StartsWith(Foreign, second);
    }

    [Fact]
    public void Empty_list_restores_original_exactly()
    {
        var applied = HostsBlockingProvider.BuildContent(Foreign, ["a.com", "b.com"], DateTime.Now);
        Assert.Equal(Foreign, HostsBlockingProvider.BuildContent(applied, [], DateTime.Now));
    }

    [Fact]
    public void Original_without_trailing_newline_is_restored()
    {
        const string original = "127.0.0.1 localhost";
        var applied = HostsBlockingProvider.BuildContent(original, ["a.com"], DateTime.Now);
        var removed = HostsBlockingProvider.StripManagedBlock(applied);
        Assert.Equal(original + "\n", removed);
    }

    [Fact]
    public void Corrupted_block_only_removes_our_entries()
    {
        var corrupted = Foreign + HostsBlockingProvider.BeginMarker + "\r\n0.0.0.0 a.com\r\n:: a.com\r\n1.2.3.4 keep.me\r\n";
        var stripped = HostsBlockingProvider.StripManagedBlock(corrupted);
        Assert.Contains("1.2.3.4 keep.me", stripped);
        Assert.DoesNotContain("a.com", stripped);
        Assert.StartsWith(Foreign, stripped);
    }

    [Fact]
    public void Block_in_middle_keeps_surrounding_lines()
    {
        var content = "line1\n" + HostsBlockingProvider.BeginMarker + "\n0.0.0.0 x.com\n" + HostsBlockingProvider.EndMarker + "\nline2\n";
        Assert.Equal("line1\nline2\n", HostsBlockingProvider.StripManagedBlock(content));
    }

    private static int CountOccurrences(string s, string sub)
    {
        var count = 0;
        for (var i = s.IndexOf(sub, StringComparison.Ordinal); i >= 0; i = s.IndexOf(sub, i + 1, StringComparison.Ordinal)) count++;
        return count;
    }
}

public class PasswordHasherTests
{
    [Fact]
    public void Hash_and_verify_roundtrip()
    {
        var hash = PasswordHasher.Hash("MatKhau123", 10_000);
        Assert.StartsWith("pbkdf2-sha256$10000$", hash);
        Assert.DoesNotContain("MatKhau123", hash);
        Assert.True(PasswordHasher.Verify("MatKhau123", hash));
        Assert.False(PasswordHasher.Verify("matkhau123", hash));
        Assert.False(PasswordHasher.Verify("", hash));
    }

    [Fact]
    public void Same_password_produces_different_salts()
    {
        Assert.NotEqual(PasswordHasher.Hash("abc12345", 10_000), PasswordHasher.Hash("abc12345", 10_000));
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData("pbkdf2-sha256$abc$x$y")]
    [InlineData("pbkdf2-sha256$100$AAAA$AAAA")]
    public void Malformed_hash_is_rejected(string stored) => Assert.False(PasswordHasher.Verify("x", stored));

    [Theory]
    [InlineData("short1", false)]
    [InlineData("onlyletters", false)]
    [InlineData("12345678", false)]
    [InlineData("hopLe2024", true)]
    public void Strength_rules(string password, bool valid) =>
        Assert.Equal(valid, PasswordHasher.ValidateStrength(password) is null);

    [Fact]
    public void Default_iterations_meet_owasp_minimum() => Assert.True(PasswordHasher.DefaultIterations >= 600_000);
}

public class BlocklistAndCatalogTests
{
    [Fact]
    public void Catalog_has_expected_unique_platforms_and_discord_in_both_groups()
    {
        Assert.Equal(25, PlatformCatalog.All.Count);
        Assert.Equal(25, PlatformCatalog.All.Select(p => p.Key).Distinct().Count());
        Assert.Equal(2, PlatformCatalog.Find("discord")!.Groups.Count);
    }

    [Fact]
    public void Catalog_domains_are_valid_and_lowercase()
    {
        foreach (var p in PlatformCatalog.All)
        foreach (var d in p.Domains)
        {
            Assert.True(DomainNormalizer.TryNormalize(d, out var n, out _), d);
            Assert.Equal(d, n);
        }
    }

    [Fact]
    public void Build_merges_platforms_and_custom_domains_sorted_distinct()
    {
        var list = BlocklistBuilder.Build(["youtube", "unknown"], ["shopee.vn", "SHOPEE.vn", "bad domain"]);
        Assert.Contains("youtube.com", list);
        Assert.Contains("youtu.be", list);
        Assert.Contains("www.shopee.vn", list);
        Assert.Contains("m.shopee.vn", list);
        Assert.Equal(list.Distinct().Count(), list.Count);
        Assert.Equal(list.OrderBy(x => x, StringComparer.Ordinal), list);
    }

    [Fact]
    public void Protected_processes_cannot_be_blocked()
    {
        Assert.True(ProtectedProcesses.IsProtected("explorer"));
        Assert.True(ProtectedProcesses.IsProtected(@"C:\Windows\System32\MsMpEng.exe"));
        Assert.False(ProtectedProcesses.IsProtected("Discord.exe"));
        Assert.Equal("Discord.exe", ProtectedProcesses.NormalizeExeName(@"""C:\Apps\Discord.exe"""));
        Assert.Equal("roblox.exe", ProtectedProcesses.NormalizeExeName("roblox"));
        Assert.Null(ProtectedProcesses.NormalizeExeName("  "));
    }
}
