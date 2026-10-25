using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CampusNetManager;

public sealed record CleanupResult(
    bool LoggedIn,
    bool MatchedDevice,
    int RemovedSessions,
    string Message
);

public sealed class SelfServiceClient
{
    private const string DefaultBaseUrl =
        "http://10.16.11.11:60001/Self";
    private static readonly Regex CheckCodePattern = new(
        "name=\"checkcode\"\\s+value=\"([^\"]+)\"",
        RegexOptions.Compiled
    );
    private static readonly Regex FormActionPattern = new(
        "<form\\s+action=\"([^\"]+)\"",
        RegexOptions.Compiled
    );

    private readonly HttpClient _client;
    private readonly string _baseUrl;

    private SelfServiceClient(string baseUrl, string? sourceIp)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        var cookies = new CookieContainer();
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            UseCookies = true,
            CookieContainer = cookies,
            UseProxy = false,
            AutomaticDecompression =
                DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        if (!string.IsNullOrWhiteSpace(sourceIp) &&
            IPAddress.TryParse(sourceIp, out var address))
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
                    socket.Bind(new IPEndPoint(address, 0));
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

        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36"
        );
    }

    public static async Task<CleanupResult> CleanupOwnSessionsAsync(
        string localAccount,
        string password,
        string deviceMac,
        string? sourceIp,
        string? baseUrl = null,
        IReadOnlyCollection<string>? trustedMacs = null,
        bool personalMode = false,
        CancellationToken cancellationToken = default)
    {
        var normalizedMac = NormalizeMac(deviceMac);
        if (string.IsNullOrWhiteSpace(localAccount) ||
            string.IsNullOrEmpty(password))
        {
            return new CleanupResult(
                false,
                false,
                0,
                "设备数已达上限，但账号或密码未配置；未注销任何设备"
            );
        }

        try
        {
            var service = new SelfServiceClient(
                string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl,
                sourceIp
            );
            var login = await service.LoginAsync(
                localAccount,
                password,
                cancellationToken
            );
            if (!login.Success)
            {
                return new CleanupResult(false, false, 0, login.Message);
            }

            var listResponse = await service._client.GetAsync(
                service._baseUrl + "/dashboard/getOnlineList",
                cancellationToken
            );
            if (!listResponse.IsSuccessStatusCode)
            {
                return new CleanupResult(
                    true,
                    false,
                    0,
                    "自助服务已登录，但读取在线设备列表失败；未注销任何设备"
                );
            }

            await using var listStream =
                await listResponse.Content.ReadAsStreamAsync(cancellationToken);
            using var listDocument = await JsonDocument.ParseAsync(
                listStream,
                cancellationToken: cancellationToken
            );
            if (listDocument.RootElement.ValueKind != JsonValueKind.Array)
            {
                return new CleanupResult(
                    true,
                    false,
                    0,
                    "自助服务返回的在线设备格式异常；未注销任何设备"
                );
            }

            var sessions = ParseSessions(listDocument.RootElement);
            var currentDeviceFound = sessions.Any(
                item => IsPlausibleMac(normalizedMac) &&
                        string.Equals(
                            item.Mac,
                            normalizedMac,
                            StringComparison.OrdinalIgnoreCase
                        )
            );
            var knownMacs = new HashSet<string>(
                (trustedMacs ?? Array.Empty<string>())
                    .Select(NormalizeMac)
                    .Where(IsPlausibleMac),
                StringComparer.OrdinalIgnoreCase
            );
            if (IsPlausibleMac(normalizedMac))
            {
                knownMacs.Add(normalizedMac);
            }

            var removals = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase
            );

            if (currentDeviceFound)
            {
                // A device logged in with two accounts: keep only its newest session.
                foreach (var group in sessions
                             .Where(item => !string.IsNullOrWhiteSpace(item.SessionId))
                             .GroupBy(item => item.Mac, StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var duplicate in group
                                 .OrderByDescending(item => item.LoginTime)
                                 .ThenByDescending(item => item.Index)
                                 .Skip(1))
                    {
                        removals.Add(duplicate.SessionId);
                    }
                }

                if (personalMode)
                {
                    // Personal build: remove unfamiliar devices, but never remove
                    // the other known owned device.
                    foreach (var session in sessions.Where(
                                 item => !knownMacs.Contains(item.Mac) &&
                                         !string.IsNullOrWhiteSpace(item.SessionId)))
                    {
                        removals.Add(session.SessionId);
                    }
                }
                else if (removals.Count == 0 && sessions.Count > 1)
                {
                    // Generic build: free exactly one slot and preserve one other
                    // online device by removing only the oldest session.
                    var oldest = sessions
                        .Where(item => !string.IsNullOrWhiteSpace(item.SessionId))
                        .OrderBy(item => item.LoginTime)
                        .ThenBy(item => item.Index)
                        .FirstOrDefault();
                    if (oldest != null)
                    {
                        removals.Add(oldest.SessionId);
                    }
                }
            }
            else
            {
                // If this device's MAC is unavailable or absent from the list,
                // free exactly one slot by removing the oldest registered session.
                removals.Clear();
                var oldest = sessions
                    .Where(item => !string.IsNullOrWhiteSpace(item.SessionId))
                    .OrderBy(item => item.LoginTime)
                    .ThenBy(item => item.Index)
                    .FirstOrDefault();
                if (oldest != null)
                {
                    removals.Add(oldest.SessionId);
                }
            }

            if (removals.Count == 0)
            {
                return new CleanupResult(
                    true,
                    false,
                    0,
                    !currentDeviceFound
                        ? "未找到本机 MAC，但在线列表中没有可注销的会话；未注销任何设备"
                        : personalMode
                        ? "设备数已达上限，但没有发现陌生 MAC 或同设备重复会话；未注销任何设备"
                        : "设备数已达上限，但在线设备不足两台或没有可安全清理的旧会话；未注销任何设备"
                );
            }

            var removed = 0;
            foreach (var sessionId in removals)
            {
                var response = await service._client.GetAsync(
                    service._baseUrl +
                    "/dashboard/tooffline?sessionid=" +
                    Uri.EscapeDataString(sessionId),
                    cancellationToken
                );
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }
                try
                {
                    await using var stream =
                        await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var document = await JsonDocument.ParseAsync(
                        stream,
                        cancellationToken: cancellationToken
                    );
                    if (document.RootElement.TryGetProperty(
                            "success",
                            out var success) &&
                        success.ValueKind == JsonValueKind.True)
                    {
                        removed++;
                    }
                }
                catch
                {
                }
            }

            return removed > 0
                ? new CleanupResult(
                    true,
                    currentDeviceFound,
                    removed,
                    !currentDeviceFound
                        ? "未找到本机 MAC，已注销登录时间最早的在线会话"
                        : personalMode
                        ? "已注销陌生 MAC 或同设备最旧的重复会话，保留受信任设备"
                        : "已注销最旧的旧会话，并保留另一台设备在线"
                )
                : new CleanupResult(
                    true,
                    currentDeviceFound,
                    0,
                    !currentDeviceFound
                        ? "已选择最旧在线会话，但自助服务拒绝注销"
                        : "找到可清理的旧会话，但自助服务拒绝注销"
                );
        }
        catch
        {
            return new CleanupResult(
                false,
                false,
                0,
                "进入自助服务失败；未注销任何设备"
            );
        }
    }

    private static List<OnlineSession> ParseSessions(JsonElement root)
    {
        var sessions = new List<OnlineSession>();
        var index = 0;
        foreach (var item in root.EnumerateArray())
        {
            var remoteMac = NormalizeMac(
                item.TryGetProperty("mac", out var macElement)
                    ? macElement.GetString() ?? ""
                    : ""
            );
            var sessionId =
                item.TryGetProperty("sessionId", out var sessionElement)
                    ? sessionElement.ValueKind == JsonValueKind.String
                        ? sessionElement.GetString()?.Trim() ?? ""
                        : sessionElement.GetRawText().Trim()
                    : "";
            var loginTime = DateTime.MinValue;
            if (item.TryGetProperty("loginTime", out var loginElement) &&
                loginElement.ValueKind == JsonValueKind.String)
            {
                DateTime.TryParse(loginElement.GetString(), out loginTime);
            }
            sessions.Add(new OnlineSession(
                sessionId,
                remoteMac,
                loginTime,
                index++
            ));
        }
        return sessions;
    }

    private async Task<LoginAttempt> LoginAsync(
        string account,
        string password,
        CancellationToken cancellationToken)
    {
        var loginPageUrl = _baseUrl + "/login/?302=LI";
        var lastError = "自助服务登录未成功";
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var pageText = await _client.GetStringAsync(
                loginPageUrl,
                cancellationToken
            );
            var checkCode = CheckCodePattern.Match(pageText);
            var action = FormActionPattern.Match(pageText);
            if (!checkCode.Success || !action.Success)
            {
                return new LoginAttempt(
                    false,
                    "自助服务页面格式发生变化；未注销任何设备"
                );
            }

            var imageBytes = await _client.GetByteArrayAsync(
                _baseUrl +
                "/login/randomCode?t=" +
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                cancellationToken
            );
            var code = CaptchaSolver.Solve(imageBytes);
            if (code == null)
            {
                lastError = "验证码识别置信度不足";
                continue;
            }

            var form = new Dictionary<string, string>
            {
                ["account"] = account,
                ["password"] = password,
                ["code"] = code,
                ["checkcode"] = checkCode.Groups[1].Value,
                ["foo"] = "",
                ["bar"] = "",
                ["submit"] = "登 录"
            };
            using var content = new FormUrlEncodedContent(form);
            var actionUrl = new Uri(new Uri(loginPageUrl), action.Groups[1].Value);
            using var response = await _client.PostAsync(
                actionUrl,
                content,
                cancellationToken
            );
            var resultText =
                await response.Content.ReadAsStringAsync(cancellationToken);
            if (IsDashboard(resultText))
            {
                return new LoginAttempt(true, "");
            }
            if (resultText.Contains("验证码错误", StringComparison.Ordinal) ||
                resultText.Contains("验证码不正确", StringComparison.Ordinal))
            {
                lastError = "验证码识别错误";
                continue;
            }
            if (resultText.Contains("账号或密码", StringComparison.Ordinal) ||
                resultText.Contains("密码出现错误", StringComparison.Ordinal))
            {
                return new LoginAttempt(
                    false,
                    "自助服务拒绝了账号或密码；未注销任何设备"
                );
            }
        }

        return new LoginAttempt(
            false,
            lastError + "；未注销任何设备"
        );
    }

    private static bool IsDashboard(string text)
    {
        return text.Contains(
                   "dashboard/getOnlineList",
                   StringComparison.Ordinal) ||
               text.Contains(
                   "id=\"OnlineLisBig\"",
                   StringComparison.Ordinal);
    }

    public static string NormalizeMac(string value)
    {
        return new string(
            value
                .Where(Uri.IsHexDigit)
                .Select(char.ToUpperInvariant)
                .ToArray()
        );
    }

    public static bool IsPlausibleMac(string value)
    {
        return value.Length == 12 &&
               value.All(Uri.IsHexDigit) &&
               value != "000000000000" &&
               value != "FFFFFFFFFFFF";
    }

    private sealed record OnlineSession(
        string SessionId,
        string Mac,
        DateTime LoginTime,
        int Index
    );

    private sealed record LoginAttempt(bool Success, string Message);
}
