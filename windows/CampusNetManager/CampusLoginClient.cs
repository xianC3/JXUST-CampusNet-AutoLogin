using System.Net;
using System.Net.Sockets;
using System.Text.Json;

namespace CampusNetManager;

public sealed record LoginResult(
    bool Success,
    bool Skipped,
    bool Retry,
    string Message,
    string? Ssid
);

public sealed class CampusLoginClient
{
    private readonly AppSettings _settings;
    private readonly bool _bindToWifi;
    private readonly Action<string> _log;

    public CampusLoginClient(
        AppSettings settings,
        bool bindToWifi = true,
        Action<string>? log = null)
    {
        _settings = settings;
        _bindToWifi = bindToWifi;
        _log = log ?? SettingsStore.Log;
    }

    public async Task<LoginResult> CheckAndLoginAsync(
        string password,
        CancellationToken cancellationToken = default,
        bool loginIfOffline = true,
        bool allowWifiSwitch = true)
    {
        if (string.IsNullOrWhiteSpace(_settings.Account) || string.IsNullOrEmpty(password))
        {
            return new LoginResult(
                false,
                false,
                false,
                "请先设置校园网账号和密码",
                null
            );
        }

        var ssid = NetworkService.GetCurrentSsid();
        var ssidKnown = !string.IsNullOrWhiteSpace(ssid);

        if (ssidKnown && !IsCampusSsid(ssid!))
        {
            var visibleCampus = NetworkService.FindVisibleCampusSsid(
                _settings.CampusSsids
            );
            if (allowWifiSwitch &&
                !string.IsNullOrWhiteSpace(visibleCampus) &&
                !string.Equals(
                    visibleCampus,
                    ssid,
                    StringComparison.OrdinalIgnoreCase) &&
                NetworkService.ConnectToWifi(visibleCampus))
            {
                return new LoginResult(
                    false,
                    false,
                    true,
                    $"检测到校园网“{visibleCampus}”，正在切换连接",
                    ssid
                );
            }
            return new LoginResult(
                true,
                true,
                false,
                $"当前网络“{ssid}”不在校园网名单，已跳过",
                ssid
            );
        }

        if (!ssidKnown && !NetworkService.IsOnWifi())
        {
            return new LoginResult(
                true,
                true,
                false,
                "当前不是 Wi-Fi 网络，已跳过",
                ssid
            );
        }

        var ip = NetworkService.GetWifiIpv4();
        if (string.IsNullOrWhiteSpace(ip))
        {
            return new LoginResult(
                false,
                false,
                true,
                "正在获取校园网地址，请稍后重试",
                ssid
            );
        }

        using var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = true,
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        // Bind portal traffic to the Wi-Fi address so a VPN cannot redirect
        // the campus login request to the wrong interface.
        if (_bindToWifi && IPAddress.TryParse(ip, out var wifiAddress))
        {
            handler.ConnectCallback = async (context, token) =>
            {
                var socket = new Socket(
                    AddressFamily.InterNetwork,
                    SocketType.Stream,
                    ProtocolType.Tcp
                );
                try
                {
                    socket.Bind(new IPEndPoint(wifiAddress, 0));
                    await socket.ConnectAsync(context.DnsEndPoint, token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            };
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(_settings.RequestTimeoutSeconds)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36"
        );

        JsonDocument? status = null;
        try
        {
            status = await GetJsonpAsync(
                client,
                BuildStatusUrl(),
                cancellationToken
            );
        }
        catch
        {
        }

        var portalRecognized = status != null;
        if (!ssidKnown && !portalRecognized)
        {
            return new LoginResult(
                false,
                false,
                true,
                "正在识别校园网连接，请保持 Wi-Fi 连接",
                ssid
            );
        }

        using (status)
        {
            if (status != null && IsOnline(status))
            {
                var uid = Text(status, "uid");
                var internet = await InternetProbe.CheckAsync(
                    ip,
                    _settings.RequestTimeoutSeconds,
                    cancellationToken
                );
                if (!internet.Available)
                {
                    return new LoginResult(
                        false,
                        false,
                        true,
                        "校园网已认证，但外网暂时不可用，正在等待网络恢复",
                        ssid
                    );
                }
                var message = ssidKnown
                    ? $"已在线且外网可用：{uid}（{internet.ElapsedMilliseconds} ms）"
                    : $"已在线且外网可用（{internet.ElapsedMilliseconds} ms）";
                return new LoginResult(true, false, false, message, ssid);
            }
        }

        if (!loginIfOffline)
        {
            return new LoginResult(
                status != null,
                false,
                status == null,
                status == null
                    ? "校园门户不可达（只读检查）"
                    : "门户可达，当前为离线状态（只读检查）",
                ssid
            );
        }

        using var login = await GetJsonpAsync(
            client,
            BuildLoginUrl(ip!, password, NetworkService.GetWifiMacAddress()),
            cancellationToken
        );
        if (login == null)
        {
            return new LoginResult(
                false,
                false,
                true,
                "校园门户暂时不可达，请稍后重试",
                ssid
            );
        }

        var result = Text(login, "result");
        var retCode = Text(login, "ret_code");
        if (result == "1" ||
            result.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
            retCode == "2")
        {
            var internet = await InternetProbe.CheckAsync(
                ip,
                _settings.RequestTimeoutSeconds,
                cancellationToken
            );
            if (!internet.Available)
            {
                return new LoginResult(
                    false,
                    false,
                    true,
                    "校园网认证成功，但外网暂时不可用，正在等待网络恢复",
                    ssid
                );
            }
            var message = ssidKnown
                ? $"登录成功且外网可用：{ip}（{internet.ElapsedMilliseconds} ms）"
                : $"登录成功且外网可用（{internet.ElapsedMilliseconds} ms）";
            return new LoginResult(true, false, false, message, ssid);
        }

        var detail = Text(login, "msg");
        if (IsDeviceLimitMessage(detail))
        {
            var localAccount = ProviderCatalog.Parse(
                _settings.Account,
                _settings.ProviderSuffix
            ).LocalAccount;
            var deviceMac = NetworkService.GetWifiMacAddress();
            var trustedMacs = TrustedMacStore.Load();
            var cleanup = await SelfServiceClient.CleanupOwnSessionsAsync(
                localAccount,
                password,
                deviceMac,
                _bindToWifi ? ip : null,
                _settings.SelfServiceBase,
                trustedMacs,
                trustedMacs.Count > 0,
                cancellationToken: cancellationToken
            );
            _log("LIMIT " + cleanup.Message);

            if (cleanup.RemovedSessions > 0)
            {
                await Task.Delay(1200, cancellationToken);
                using var retry = await GetJsonpAsync(
                    client,
                    BuildLoginUrl(ip!, password, deviceMac),
                    cancellationToken
                );
                if (retry != null &&
                    (IsLoginSuccess(retry) ||
                     Text(retry, "ret_code") == "2"))
                {
                    var internet = await InternetProbe.CheckAsync(
                        ip,
                        _settings.RequestTimeoutSeconds,
                        cancellationToken
                    );
                    if (!internet.Available)
                    {
                        return new LoginResult(
                            false,
                            false,
                            true,
                            "已清理本机旧登录，但外网暂时不可用",
                            ssid
                        );
                    }
                    return new LoginResult(
                        true,
                        false,
                        false,
                        $"已清理本机旧登录，连接及外网均正常：{ip}",
                        ssid
                    );
                }
                detail = cleanup.Message + "；重新登录仍未成功";
            }
            else
            {
                detail = cleanup.Message;
            }
        }
        return new LoginResult(
            false,
            false,
            false,
            string.IsNullOrWhiteSpace(detail)
                ? "门户拒绝了登录请求"
                : detail,
            ssid
        );
    }

    private bool IsCampusSsid(string ssid)
    {
        return _settings.CampusSsids.Any(
            x => string.Equals(x, ssid, StringComparison.OrdinalIgnoreCase)
        );
    }

    private string BuildStatusUrl()
    {
        var random = Random.Shared.Next(500, 10500);
        return $"http://{_settings.PortalHost}/drcom/chkstatus" +
               $"?callback=dr{random}&jsVersion=4.X&v={random}&lang=zh";
    }

    private string BuildLoginUrl(string ip, string password, string mac)
    {
        var random = Random.Shared.Next(500, 10500);
        var query = new Dictionary<string, string>
        {
            ["login_method"] = "1",
            ["user_account"] = _settings.Account,
            ["user_password"] = password,
            ["wlan_user_ip"] = ip,
            ["wlan_user_ipv6"] = "",
            ["wlan_user_mac"] = mac,
            ["wlan_ac_ip"] = _settings.PortalHost,
            ["wlan_ac_name"] = "",
            ["terminal_type"] = "1",
            ["lang"] = "zh",
            ["jsVersion"] = "4.X",
            ["callback"] = $"dr{random}",
            ["v"] = random.ToString()
        };
        var encoded = string.Join(
            "&",
            query.Select(
                pair =>
                    $"{Uri.EscapeDataString(pair.Key)}=" +
                    $"{Uri.EscapeDataString(pair.Value)}"
            )
        );
        return $"http://{_settings.PortalHost}:{_settings.PortalPort}" +
               $"/eportal/portal/login?{encoded}";
    }

    private static async Task<JsonDocument?> GetJsonpAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Referrer =
                new Uri("http://" + new Uri(url).Host + "/a79.htm");
            using var response =
                await client.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var text =
                await response.Content.ReadAsStringAsync(cancellationToken);
            var start = text.IndexOf('(');
            var end = text.LastIndexOf(')');
            if (start >= 0 && end > start)
            {
                text = text[(start + 1)..end];
            }
            return JsonDocument.Parse(text);
        }
        catch
        {
            return null;
        }
    }

    private static string Text(JsonDocument document, string name)
    {
        return document.RootElement.TryGetProperty(name, out var value)
            ? value.ToString()
            : "";
    }

    private static bool IsOnline(JsonDocument document)
    {
        var result = Text(document, "result");
        return result == "1" ||
               result.Equals("ok", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLoginSuccess(JsonDocument document)
    {
        var result = Text(document, "result");
        return result == "1" ||
               result.Equals("ok", StringComparison.OrdinalIgnoreCase) ||
               Text(document, "ret_code") == "2";
    }

    public static bool IsDeviceLimitMessage(string message)
    {
        return message.Contains("上限", StringComparison.Ordinal) ||
               message.Contains("Limit Users", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("设备数", StringComparison.Ordinal) ||
               message.Contains("终端数", StringComparison.Ordinal) ||
               message.Contains("登录数", StringComparison.Ordinal);
    }
}
