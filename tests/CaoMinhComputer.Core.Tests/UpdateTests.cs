using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CaoMinhComputer.Core.Services;

namespace CaoMinhComputer.Core.Tests;

/// <summary>Kiểm tra chữ ký bản cập nhật: chỉ manifest ký đúng khóa, đúng sản phẩm, đúng file mới được chấp nhận.</summary>
public class UpdateTests
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly string[] Trusted = [Convert.ToBase64String(Key.ExportSubjectPublicKeyInfo())];

    private static byte[] Manifest(string version = "1.5.0", string product = "ConNgoan", string sha = "", long size = 1234) =>
        JsonSerializer.SerializeToUtf8Bytes(new UpdateManifest(product, version, "https://caominhcomputer.com/x.exe",
            sha.Length == 0 ? new string('a', 64) : sha, size, "2026-11-20", "Sửa lỗi"));

    [Fact]
    public void Valid_signature_is_accepted()
    {
        var bytes = Manifest();
        var m = UpdateVerifier.Verify(bytes, UpdateVerifier.Sign(bytes, Key), Trusted, out var error);
        Assert.Null(error);
        Assert.NotNull(m);
        Assert.Equal("1.5.0", m!.Version);
        Assert.Equal("Sửa lỗi", m.Notes);
    }

    [Fact]
    public void Modified_manifest_is_rejected()
    {
        var bytes = Manifest();
        var sig = UpdateVerifier.Sign(bytes, Key);
        var tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(bytes).Replace("x.exe", "y.exe"));
        Assert.Null(UpdateVerifier.Verify(tampered, sig, Trusted, out var error));
        Assert.Contains("Chữ ký", error);
    }

    [Fact]
    public void Signature_from_other_key_is_rejected()
    {
        using var attacker = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var bytes = Manifest();
        Assert.Null(UpdateVerifier.Verify(bytes, UpdateVerifier.Sign(bytes, attacker), Trusted, out _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("không-phải-base64")]
    [InlineData("AAAA")]
    public void Garbage_signature_is_rejected(string sig) =>
        Assert.Null(UpdateVerifier.Verify(Manifest(), sig, Trusted, out _));

    [Fact]
    public void Wrong_product_or_bad_fields_are_rejected()
    {
        foreach (var bytes in new[] { Manifest(product: "Khac"), Manifest(version: "abc"), Manifest(sha: "xyz"), Manifest(size: 0) })
            Assert.Null(UpdateVerifier.Verify(bytes, UpdateVerifier.Sign(bytes, Key), Trusted, out _));
    }

    [Fact]
    public void Production_key_is_a_valid_p256_public_key()
    {
        Assert.NotEmpty(UpdateKeys.Trusted);
        foreach (var k in UpdateKeys.Trusted)
        {
            using var e = ECDsa.Create();
            e.ImportSubjectPublicKeyInfo(Convert.FromBase64String(k), out _);
            Assert.Equal(256, e.KeySize);
        }
        // Manifest ký bằng khóa thử nghiệm không được chấp nhận bởi khóa thật.
        var bytes = Manifest();
        Assert.Null(UpdateVerifier.Verify(bytes, UpdateVerifier.Sign(bytes, Key), out _));
    }

    [Theory]
    [InlineData("1.4.1", "1.4.0", true)]
    [InlineData("1.4.0.1", "1.4.0", true)]
    [InlineData("1.5", "1.4.9", true)]
    [InlineData("1.4.0", "1.4.0", false)]
    [InlineData("1.3.9", "1.4.0", false)]
    [InlineData("1.4.0.0", "1.4.0", false)]
    public void Only_newer_versions_are_offered(string offered, string current, bool expected)
    {
        var m = new UpdateManifest("ConNgoan", offered, "https://a/b.exe", new string('a', 64), 1);
        Assert.Equal(expected, UpdateVerifier.IsNewer(m, Version.Parse(current)));
    }

    [Fact]
    public void Downloaded_file_must_match_size_and_hash()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("installer-content"));
            var sha = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
            var good = new UpdateManifest("ConNgoan", "1.5.0", "https://a/b.exe", sha.ToLowerInvariant(), 17);
            Assert.True(UpdateVerifier.VerifyFile(path, good, out _));
            Assert.False(UpdateVerifier.VerifyFile(path, good with { Size = 18 }, out _));
            Assert.False(UpdateVerifier.VerifyFile(path, good with { Sha256 = new string('b', 64) }, out var error));
            Assert.Contains("SHA-256", error);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
