using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CampusNetManager;

var failures = new List<string>();
void Check(string name, bool condition, string detail)
{
    Console.WriteLine($"{(condition ? "PASS" : "FAIL")} | {name} | {detail}");
    if (!condition)
    {
        failures.Add(name);
    }
}

if (args.Length > 0 && File.Exists(args[0]))
{
    var liveCaptcha = CaptchaSolver.Solve(File.ReadAllBytes(args[0]));
    Console.WriteLine(
        $"CAPTCHA_FILE_RESULT={liveCaptcha ?? "UNRECOGNIZED"}"
    );
    return liveCaptcha == null ? 1 : 0;
}

var captchaBytes = CreateCaptcha("4046");
Check(
    "验证码识别",
    CaptchaSolver.Solve(captchaBytes) == "4046",
    "固定验证码 4046"
);
Check(
    "设备上限错误识别",
    CampusLoginClient.IsDeviceLimitMessage(
        "您的登陆数已上限，请登陆自助服务下线其他终端后重新登陆Limit Users Err"
    ) &&
    !CampusLoginClient.IsDeviceLimitMessage("账号或密码错误"),
    "匹配实际门户错误文本"
);

using var server = new MockSelfServiceServer(captchaBytes);
server.Start();

server.OwnMac = "AABBCCDDEEFF";
server.OwnSessionId = "55";
server.Sessions.Clear();
server.Sessions.Add(new MockSession(
    "AABBCCDDEEFF", "55", "2026-10-21 12:20:00"));
server.Sessions.Add(new MockSession(
    "998877665544", "777", "2026-10-21 12:10:00"));
server.OfflineCalls.Clear();
var ownResult = await SelfServiceClient.CleanupOwnSessionsAsync(
    "2026000001",
    "test-password",
    "AA:BB:CC:DD:EE:FF",
    sourceIp: null,
    baseUrl: server.BaseUrl
);
Check(
    "同 MAC 精确注销",
    ownResult.RemovedSessions == 1 &&
    server.OfflineCalls.SequenceEqual(new[] { "777" }),
    $"注销次数={ownResult.RemovedSessions}，session={string.Join(",", server.OfflineCalls)}"
);

server.OwnMac = "112233445566";
server.OwnSessionId = "99";
server.Sessions.Clear();
server.Sessions.Add(new MockSession(
    "AABBCCDDEEFF", "55", "2026-10-21 12:20:00"));
server.Sessions.Add(new MockSession(
    "112233445566", "99", "2026-10-21 12:10:00"));
server.OfflineCalls.Clear();
var mismatch = await SelfServiceClient.CleanupOwnSessionsAsync(
    "2026000001",
    "test-password",
    "AABBCCDDEEFF",
    sourceIp: null,
    baseUrl: server.BaseUrl,
    trustedMacs: new[] { "AABBCCDDEEFF", "112233445566" },
    personalMode: true
);
Check(
    "不同 MAC 禁止注销",
    !mismatch.MatchedDevice &&
    mismatch.RemovedSessions == 0 &&
    server.OfflineCalls.Count == 0,
    "无关设备未被操作"
);

server.Sessions.Clear();
server.Sessions.Add(new MockSession(
    "AABBCCDDEEFF", "55", "2026-10-21 12:20:00"));
server.Sessions.Add(new MockSession(
    "AABBCCDDEEFF", "56", "2026-10-21 12:10:00"));
server.Sessions.Add(new MockSession(
    "998877665544", "777", "2026-10-21 12:15:00"));
server.OfflineCalls.Clear();
var personalCleanup = await SelfServiceClient.CleanupOwnSessionsAsync(
    "2026000001",
    "test-password",
    "AABBCCDDEEFF",
    sourceIp: null,
    baseUrl: server.BaseUrl,
    trustedMacs: new[] { "AABBCCDDEEFF", "A1B2C3D4E5F6" },
    personalMode: true
);
Check(
    "专用版陌生与重复会话",
    personalCleanup.RemovedSessions == 2 &&
    server.OfflineCalls.OrderBy(value => value)
        .SequenceEqual(new[] { "56", "777" }),
    $"注销={string.Join(",", server.OfflineCalls)}"
);

var loginCount = server.LoginCount;
var missingMac = await SelfServiceClient.CleanupOwnSessionsAsync(
    "2026000001",
    "test-password",
    "",
    sourceIp: null,
    baseUrl: server.BaseUrl
);
Check(
    "缺少 MAC 时停止",
    !missingMac.LoggedIn &&
    server.LoginCount == loginCount,
    "未尝试登录自助服务"
);

using var portal = new MockPortalServer(server);
server.OwnMac = NetworkService.GetWifiMacAddress();
server.OwnSessionId = "55";
server.Sessions.Clear();
server.Sessions.Add(new MockSession(
    server.OwnMac, "55", "2026-10-21 12:20:00"));
server.Sessions.Add(new MockSession(
    "112233445566", "99", "2026-10-21 12:10:00"));
server.OfflineCalls.Clear();
portal.LoginCount = 0;
var integrationSettings = new AppSettings
{
    PortalHost = "127.0.0.1",
    PortalPort = portal.Port,
    Account = "2026000001@unicomyt",
    ProviderSuffix = "@unicomyt",
    SelfServiceBase = server.BaseUrl,
    CampusSsids = new List<string>
    {
        NetworkService.GetCurrentSsid() ?? ""
    },
    AutoLoginEnabled = true,
    RequestTimeoutSeconds = 5,
    WifiWaitSeconds = 5
};
var integrationResult = await new CampusLoginClient(
    integrationSettings,
    bindToWifi: false,
    log: _ => { }
).CheckAndLoginAsync("test-password");
Check(
    "设备上限端到端恢复",
    integrationResult.Success &&
    integrationResult.Message.Contains("清理本机旧登录", StringComparison.Ordinal) &&
    portal.LoginCount == 2 &&
    server.OfflineCalls.SequenceEqual(new[] { "99" }),
    $"消息={integrationResult.Message}，门户登录次数={portal.LoginCount}，" +
    $"自助登录次数={server.LoginCount}，注销={string.Join(",", server.OfflineCalls)}"
);

var liveSettings = SettingsStore.Load();
var currentSsid = NetworkService.GetCurrentSsid();
var visibleCampus = NetworkService.FindVisibleCampusSsid(
    liveSettings.CampusSsids
);
Check(
    "可见校园网扫描",
    !string.IsNullOrWhiteSpace(visibleCampus) ||
    liveSettings.CampusSsids.Contains(
        currentSsid ?? "",
        StringComparer.OrdinalIgnoreCase),
    $"当前={currentSsid ?? "(none)"}，可见校园网={visibleCampus ?? "(none)"}"
);
var internet = await InternetProbe.CheckAsync(
    NetworkService.GetWifiIpv4(),
    timeoutSeconds: 6
);
Check(
    "当前 Wi-Fi 外网",
    internet.Available,
    $"{internet.ElapsedMilliseconds} ms，{internet.Detail}"
);

Console.WriteLine(
    failures.Count == 0
        ? "RESULT all smoke tests passed"
        : $"RESULT {failures.Count} failed"
);
return failures.Count == 0 ? 0 : 1;

static byte[] CreateCaptcha(string digits)
{
    string[][] templates =
    {
        new[]
        {
            "000011111000", "000011111000", "000111111100",
            "001110001110", "111100000111", "111100000111",
            "111100000111", "111100000111", "111100000111",
            "111100000111", "111100000111", "001110001110",
            "000111111100", "000011111000"
        },
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        new[]
        {
            "000000111100", "000000111100", "000001111100",
            "000011111100", "000011011100", "000110011100",
            "001110011100", "001100011100", "111000011100",
            "111111111111", "111111111111", "000000011100",
            "000000011100", "000000011100"
        },
        Array.Empty<string>(),
        new[]
        {
            "000111111100", "000111111100", "001111111110",
            "111100000010", "111000000000", "111111111100",
            "111111111110", "111100011111", "111000001111",
            "111000001111", "111000001111", "111100011111",
            "111111111110", "000111111100"
        }
    };

    using var bitmap = new Bitmap(60, 20, PixelFormat.Format24bppRgb);
    using var graphics = Graphics.FromImage(bitmap);
    graphics.Clear(Color.FromArgb(250, 250, 250));
    using var brush = new SolidBrush(Color.FromArgb(55, 72, 105));
    for (var index = 0; index < 4; index++)
    {
        var digit = digits[index] - '0';
        var rows = templates[digit];
        for (var y = 0; y < rows.Length; y++)
        {
            for (var x = 0; x < rows[y].Length; x++)
            {
                if (rows[y][x] == '1')
                {
                    graphics.FillRectangle(
                        brush,
                        index * 15 + x + 1,
                        y + 3,
                        1,
                        1
                    );
                }
            }
        }
    }
    using var stream = new MemoryStream();
    bitmap.Save(stream, ImageFormat.Png);
    return stream.ToArray();
}

sealed class MockSelfServiceServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly byte[] _captcha;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public MockSelfServiceServer(byte[] captcha)
    {
        _captcha = captcha;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        BaseUrl = $"http://127.0.0.1:{port}/Self";
        _loop = Task.Run(LoopAsync);
    }

    public string BaseUrl { get; }
    public string OwnMac { get; set; } = "AABBCCDDEEFF";
    public string OwnSessionId { get; set; } = "55";
    public List<MockSession> Sessions { get; } = new();
    public int LoginCount { get; private set; }
    public ConcurrentQueue<string> OfflineCalls { get; } = new();

    public void Start()
    {
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(
                   stream,
                   Encoding.UTF8,
                   detectEncodingFromByteOrderMarks: false,
                   leaveOpen: true))
        {
            var requestLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }
            var parts = requestLine.Split(' ');
            var method = parts[0];
            var target = parts[1];
            var headers = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase
            );
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
                var separator = line.IndexOf(':');
                if (separator > 0)
                {
                    headers[line[..separator].Trim()] =
                        line[(separator + 1)..].Trim();
                }
            }

            var body = "";
            if (headers.TryGetValue("Content-Length", out var lengthText) &&
                int.TryParse(lengthText, out var length))
            {
                var buffer = new char[length];
                var read = 0;
                while (read < length)
                {
                    var count = await reader.ReadAsync(
                        buffer.AsMemory(read, length - read)
                    );
                    if (count == 0)
                    {
                        break;
                    }
                    read += count;
                }
                body = new string(buffer, 0, read);
            }

            var path = new Uri("http://127.0.0.1" + target).AbsolutePath;
            if (method == "GET" && path == "/Self/login/")
            {
                await WriteAsync(
                    stream,
                    200,
                    "text/html",
                    "<form action=\"/Self/login/verify\" method=\"post\">" +
                    "<input type=\"hidden\" name=\"checkcode\" value=\"5094\">" +
                    "</form>"
                );
            }
            else if (method == "GET" &&
                     path == "/Self/login/randomCode")
            {
                await WriteAsync(stream, 200, "image/png", _captcha);
            }
            else if (method == "POST" && path == "/Self/login/verify")
            {
                LoginCount++;
                var form = ParseForm(body);
                if (form.GetValueOrDefault("code") == "4046")
                {
                    await WriteAsync(
                        stream,
                        302,
                        "text/plain",
                        Array.Empty<byte>(),
                        new Dictionary<string, string>
                        {
                            ["Location"] = "/Self/dashboard",
                            ["Set-Cookie"] = "JSESSIONID=test; Path=/Self"
                        }
                    );
                }
                else
                {
                    await WriteAsync(
                        stream,
                        200,
                        "text/html",
                        "验证码错误"
                    );
                }
            }
            else if (method == "GET" && path == "/Self/dashboard")
            {
                await WriteAsync(
                    stream,
                    200,
                    "text/html",
                    "<script>url: \"dashboard/getOnlineList\"</script>" +
                    "<table id=\"OnlineLisBig\"></table>"
                );
            }
            else if (method == "GET" &&
                     path == "/Self/dashboard/getOnlineList")
            {
                if (Sessions.Count == 0)
                {
                    Sessions.Add(new MockSession(
                        OwnMac,
                        OwnSessionId,
                        "2026-10-21 12:20:00"
                    ));
                    Sessions.Add(new MockSession(
                        "998877665544",
                        "777",
                        "2026-10-21 12:10:00"
                    ));
                }
                var json = JsonSerializer.Serialize(
                    Sessions.Select(item => new
                    {
                        mac = item.Mac,
                        sessionId = item.Id,
                        loginTime = item.LoginTime
                    })
                );
                await WriteAsync(stream, 200, "application/json", json);
            }
            else if (method == "GET" &&
                     path == "/Self/dashboard/tooffline")
            {
                var query = new Uri("http://127.0.0.1" + target).Query;
                var values = ParseForm(query.TrimStart('?'));
                var session = values.GetValueOrDefault("sessionid", "");
                OfflineCalls.Enqueue(session);
                await WriteAsync(
                    stream,
                    200,
                    "application/json",
                    "{\"success\":" +
                    (Sessions.Any(item => item.Id == session) ? "true" : "false") +
                    "}"
                );
            }
            else
            {
                await WriteAsync(stream, 404, "text/plain", "not found");
            }
        }
    }

    private static Dictionary<string, string> ParseForm(string body)
    {
        return body.Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                pair => Uri.UnescapeDataString(
                    pair[0].Replace('+', ' ')
                ),
                pair => pair.Length > 1
                    ? Uri.UnescapeDataString(pair[1].Replace('+', ' '))
                    : ""
            );
    }

    private static async Task WriteAsync(
        NetworkStream stream,
        int status,
        string contentType,
        string text,
        Dictionary<string, string>? headers = null)
    {
        await WriteAsync(
            stream,
            status,
            contentType,
            Encoding.UTF8.GetBytes(text),
            headers
        );
    }

    private static async Task WriteAsync(
        NetworkStream stream,
        int status,
        string contentType,
        byte[] bytes,
        Dictionary<string, string>? headers = null)
    {
        var reason = status switch
        {
            200 => "OK",
            302 => "Found",
            404 => "Not Found",
            _ => "Response"
        };
        var builder = new StringBuilder()
            .Append($"HTTP/1.1 {status} {reason}\r\n")
            .Append($"Content-Type: {contentType}\r\n")
            .Append($"Content-Length: {bytes.Length}\r\n")
            .Append("Connection: close\r\n");
        foreach (var header in headers ?? new Dictionary<string, string>())
        {
            builder.Append($"{header.Key}: {header.Value}\r\n");
        }
        builder.Append("\r\n");
        var head = Encoding.ASCII.GetBytes(builder.ToString());
        await stream.WriteAsync(head);
        await stream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _stop.Dispose();
    }
}

sealed record MockSession(string Mac, string Id, string LoginTime);

sealed class MockPortalServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly MockSelfServiceServer _selfService;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _loop;

    public MockPortalServer(MockSelfServiceServer selfService)
    {
        _selfService = selfService;
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _loop = Task.Run(LoopAsync);
    }

    public int Port { get; }
    public int LoginCount { get; set; }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_stop.Token);
            }
            catch
            {
                break;
            }
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        using (var reader = new StreamReader(
                   stream,
                   Encoding.ASCII,
                   detectEncodingFromByteOrderMarks: false,
                   leaveOpen: true))
        {
            var requestLine = await reader.ReadLineAsync();
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }
            var method = requestLine.Split(' ')[0];
            var target = requestLine.Split(' ')[1];
            string? line;
            while (!string.IsNullOrEmpty(line = await reader.ReadLineAsync()))
            {
            }
            var path = new Uri("http://127.0.0.1" + target).AbsolutePath;
            var body = "";
            if (method == "GET" && path == "/drcom/chkstatus")
            {
                body = "dr1({\"result\":0})";
            }
            else if (method == "GET" && path == "/eportal/portal/login")
            {
                LoginCount++;
                body = !_selfService.OfflineCalls.IsEmpty
                    ? "dr2({\"result\":1,\"msg\":\"ok\"})"
                    : "dr2({\"result\":0,\"ret_code\":1," +
                      "\"msg\":\"您的登陆数已上限，请登陆自助服务下线其他终端后重新登陆Limit Users Err\"})";
            }
            else
            {
                await WriteAsync(stream, 404, "not found");
                return;
            }
            await WriteAsync(stream, 200, body);
        }
    }

    private static async Task WriteAsync(
        NetworkStream stream,
        int status,
        string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var reason = status == 200 ? "OK" : "Not Found";
        var head = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {status} {reason}\r\n" +
            "Content-Type: application/javascript\r\n" +
            $"Content-Length: {bytes.Length}\r\n" +
            "Connection: close\r\n\r\n"
        );
        await stream.WriteAsync(head);
        await stream.WriteAsync(bytes);
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Stop();
        try
        {
            _loop.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }
        _stop.Dispose();
    }
}
