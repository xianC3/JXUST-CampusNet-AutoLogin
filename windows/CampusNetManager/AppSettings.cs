using System.Text.Json.Serialization;

namespace CampusNetManager;

public sealed class AppSettings
{
    public string PortalHost { get; set; } = "172.26.3.60";
    public int PortalPort { get; set; } = 801;
    public string Account { get; set; } = "";
    public string ProviderSuffix { get; set; } = "";
    public string SelfServiceBase { get; set; } =
        "http://10.16.11.11:60001/Self";
    public List<string> CampusSsids { get; set; } = new();
    public bool AutoLoginEnabled { get; set; } = true;
    public string InterfaceName { get; set; } = "WLAN";
    public int RequestTimeoutSeconds { get; set; } = 8;
    public int WifiWaitSeconds { get; set; } = 45;
}
