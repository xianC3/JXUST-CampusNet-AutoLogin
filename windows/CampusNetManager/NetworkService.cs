using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace CampusNetManager;

public static class NetworkService
{
    static NetworkService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static string? GetCurrentSsid()
    {
        try
        {
            var output = RunNetsh();
            var match = Regex.Match(
                output,
                @"(?im)^\s*SSID\s*:\s*(.+?)\s*$",
                RegexOptions.IgnoreCase
            );
            if (!match.Success)
            {
                return null;
            }

            var ssid = match.Groups[1].Value.Trim();
            if (ssid.Length >= 2 && ssid.StartsWith("\"") && ssid.EndsWith("\""))
            {
                ssid = ssid[1..^1];
            }
            return string.IsNullOrWhiteSpace(ssid) || ssid == "<unknown ssid>" ? null : ssid;
        }
        catch
        {
            return null;
        }
    }

    public static bool IsOnWifi()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces().Any(
                n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                     n.OperationalStatus == OperationalStatus.Up
            );
        }
        catch
        {
            return false;
        }
    }

    public static string? GetLocalIpv4()
    {
        try
        {
            var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up)
                .OrderByDescending(
                    n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211);

            foreach (var networkInterface in interfaces)
            {
                foreach (var address in networkInterface.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address.Address) &&
                        !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    {
                        return address.Address.ToString();
                    }
                }
            }
        }
        catch
        {
        }
        return null;
    }

    public static string? GetWifiIpv4()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(
                    n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                         n.OperationalStatus == OperationalStatus.Up
                )
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(
                    address => address.Address.AddressFamily == AddressFamily.InterNetwork &&
                               !IPAddress.IsLoopback(address.Address) &&
                               !address.Address.ToString().StartsWith(
                                   "169.254.",
                                   StringComparison.Ordinal
                               )
                )
                .Select(address => address.Address.ToString())
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    public static string GetWifiMacAddress()
    {
        try
        {
            var address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(
                    n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                         n.OperationalStatus == OperationalStatus.Up
                )
                .Select(n => n.GetPhysicalAddress())
                .FirstOrDefault(value => value.GetAddressBytes().Length == 6);
            return address == null
                ? ""
                : string.Concat(address.GetAddressBytes().Select(part => part.ToString("X2")));
        }
        catch
        {
            return "";
        }
    }

    public static string? FindVisibleCampusSsid(
        IEnumerable<string> campusSsids)
    {
        try
        {
            var output = RunNetsh("wlan show networks mode=bssid");
            var visible = new HashSet<string>(
                Regex.Matches(
                        output,
                        @"(?im)^\s*SSID\s+\d+\s*:\s*(.+?)\s*$"
                    )
                    .Select(match => match.Groups[1].Value.Trim())
                    .Where(value => !string.IsNullOrWhiteSpace(value)),
                StringComparer.OrdinalIgnoreCase
            );
            return campusSsids.FirstOrDefault(
                candidate => visible.Contains(candidate)
            );
        }
        catch
        {
            return null;
        }
    }

    public static bool ConnectToWifi(string ssid)
    {
        try
        {
            var startInfo = new ProcessStartInfo("netsh.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("wlan");
            startInfo.ArgumentList.Add("connect");
            startInfo.ArgumentList.Add($"name={ssid}");
            startInfo.ArgumentList.Add($"ssid={ssid}");
            startInfo.ArgumentList.Add("interface=WLAN");
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }
            process.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string RunNetsh(string arguments = "wlan show interfaces")
    {
        var encoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        var startInfo = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = encoding,
            StandardErrorEncoding = encoding
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return "";
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit(5000);
        return output;
    }
}
