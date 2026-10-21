namespace CampusNetManager;

public sealed record ProviderOption(string Name, string Suffix);

public static class ProviderCatalog
{
    public static readonly IReadOnlyList<ProviderOption> All = new[]
    {
        new ProviderOption("校园网", ""),
        new ProviderOption("联通", "@unicomyt"),
        new ProviderOption("移动", "@cmccyt"),
        new ProviderOption("电信", "@telecomyt")
    };

    public static ProviderOption FindBySuffix(string? suffix)
    {
        var normalized = (suffix ?? "").Trim();
        return All.FirstOrDefault(
            item => string.Equals(item.Suffix, normalized, StringComparison.OrdinalIgnoreCase)
        ) ?? All[0];
    }

    public static string NormalizeSuffix(string? suffix)
    {
        return FindBySuffix(suffix).Suffix;
    }

    public static (string LocalAccount, string Suffix) Parse(string account, string? providerSuffix)
    {
        var trimmed = (account ?? "").Trim();
        foreach (var provider in All)
        {
            if (!string.IsNullOrEmpty(provider.Suffix) &&
                trimmed.EndsWith(provider.Suffix, StringComparison.OrdinalIgnoreCase))
            {
                return (
                    trimmed[..^provider.Suffix.Length],
                    provider.Suffix
                );
            }
        }

        return (trimmed, FindBySuffix(providerSuffix).Suffix);
    }

    public static string Build(string localAccount, string? providerSuffix)
    {
        var local = (localAccount ?? "").Trim();
        foreach (var provider in All)
        {
            if (!string.IsNullOrEmpty(provider.Suffix) &&
                local.EndsWith(provider.Suffix, StringComparison.OrdinalIgnoreCase))
            {
                local = local[..^provider.Suffix.Length];
                break;
            }
        }
        return local + NormalizeSuffix(providerSuffix);
    }
}
