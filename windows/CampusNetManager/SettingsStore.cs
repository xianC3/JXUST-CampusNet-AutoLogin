using System.Text;
using System.Text.Json;

namespace CampusNetManager;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static readonly string UserProfileDirectory =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string LegacyDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CampusNetAutoLogin"
    );

    public static string DataDirectory =>
        Path.Combine(UserProfileDirectory, ".campusnet-manager");

    public static string ConfigPath => Path.Combine(DataDirectory, "config.json");
    public static string CredentialPath => Path.Combine(DataDirectory, "credential.dat");
    public static string LogPath => Path.Combine(DataDirectory, "campusnet.log");

    public static AppSettings Load()
    {
        EnsureDataDirectory();
        if (!File.Exists(ConfigPath))
        {
            var defaults = new AppSettings();
            Normalize(defaults);
            return defaults;
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(ConfigPath, Encoding.UTF8),
                JsonOptions
            ) ?? new AppSettings();
            Normalize(settings);
            return settings;
        }
        catch
        {
            Log("配置文件读取失败，已使用默认配置");
            var defaults = new AppSettings();
            Normalize(defaults);
            return defaults;
        }
    }

    public static void Save(AppSettings settings)
    {
        EnsureDataDirectory();
        Normalize(settings);
        var temp = ConfigPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions), Encoding.UTF8);
        File.Move(temp, ConfigPath, true);
    }

    public static void Log(string message)
    {
        try
        {
            EnsureDataDirectory();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}";
            File.AppendAllText(LogPath, line, Encoding.UTF8);
        }
        catch
        {
        }
    }

    public static void EnsureDataDirectory()
    {
        Directory.CreateDirectory(DataDirectory);
        try
        {
            if (!File.Exists(ConfigPath) &&
                File.Exists(Path.Combine(LegacyDataDirectory, "config.json")))
            {
                File.Copy(
                    Path.Combine(LegacyDataDirectory, "config.json"),
                    ConfigPath
                );
            }
            if (!File.Exists(CredentialPath) &&
                File.Exists(Path.Combine(LegacyDataDirectory, "credential.dat")))
            {
                File.Copy(
                    Path.Combine(LegacyDataDirectory, "credential.dat"),
                    CredentialPath
                );
            }
        }
        catch
        {
        }
    }

    private static void Normalize(AppSettings settings)
    {
        settings.PortalHost = string.IsNullOrWhiteSpace(settings.PortalHost)
            ? "172.26.3.60"
            : settings.PortalHost.Trim();
        settings.PortalPort = settings.PortalPort <= 0 ? 801 : settings.PortalPort;
        settings.Account = (settings.Account ?? "").Trim();
        var provider = ProviderCatalog.Parse(settings.Account, settings.ProviderSuffix);
        settings.ProviderSuffix = provider.Suffix;
        settings.SelfServiceBase = string.IsNullOrWhiteSpace(settings.SelfServiceBase)
            ? "http://10.16.11.11:60001/Self"
            : settings.SelfServiceBase.Trim().TrimEnd('/');
        settings.CampusSsids ??= new List<string>();
        settings.CampusSsids = settings.CampusSsids
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        settings.InterfaceName = string.IsNullOrWhiteSpace(settings.InterfaceName)
            ? "WLAN"
            : settings.InterfaceName;
        settings.RequestTimeoutSeconds = settings.RequestTimeoutSeconds <= 0
            ? 8
            : settings.RequestTimeoutSeconds;
        settings.WifiWaitSeconds = settings.WifiWaitSeconds <= 0
            ? 45
            : settings.WifiWaitSeconds;
    }
}
