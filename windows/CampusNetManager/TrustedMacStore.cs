namespace CampusNetManager;

public static class TrustedMacStore
{
    private static readonly string PathName = Path.Combine(
        AppContext.BaseDirectory,
        "trusted-macs.txt"
    );

    public static IReadOnlyCollection<string> Load()
    {
        try
        {
            if (!File.Exists(PathName))
            {
                return Array.Empty<string>();
            }
            return File.ReadAllLines(PathName)
                .SelectMany(
                    line => line.Split(
                        new[] { ',', ';', ' ', '\t' },
                        StringSplitOptions.RemoveEmptyEntries
                    )
                )
                .Select(SelfServiceClient.NormalizeMac)
                .Where(SelfServiceClient.IsPlausibleMac)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
