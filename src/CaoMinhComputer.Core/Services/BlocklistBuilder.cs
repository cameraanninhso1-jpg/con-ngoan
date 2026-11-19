namespace CaoMinhComputer.Core.Services;

/// <summary>Tính danh sách tên miền cuối cùng cần ghi vào HOSTS từ cấu hình.</summary>
public static class BlocklistBuilder
{
    public static IReadOnlyList<string> Build(IEnumerable<string> enabledPlatformKeys, IEnumerable<string> customDomains)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var key in enabledPlatformKeys)
        {
            var p = PlatformCatalog.Find(key);
            if (p is null) continue;
            foreach (var d in p.Domains) set.Add(d.ToLowerInvariant());
        }
        foreach (var raw in customDomains)
        {
            if (!DomainNormalizer.TryNormalize(raw, out var d, out _)) continue;
            foreach (var v in DomainNormalizer.ExpandVariants(d)) set.Add(v);
        }
        return set.ToList();
    }
}
