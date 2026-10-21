using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace CampusNetManager;

public sealed record InternetProbeResult(
    bool Available,
    long ElapsedMilliseconds,
    string Detail
);

public static class InternetProbe
{
    private static readonly (string Url, string ExpectedText)[] Endpoints =
    {
        ("https://www.baidu.com", ""),
        ("http://connect.rom.miui.com/generate_204", ""),
        ("http://www.msftconnecttest.com/connecttest.txt", "Microsoft Connect Test"),
        ("http://captive.apple.com/hotspot-detect.html", "Success")
    };

    public static async Task<InternetProbeResult> CheckAsync(
        string? sourceIp,
        int timeoutSeconds = 5,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        linked.CancelAfter(TimeSpan.FromSeconds(Math.Max(2, timeoutSeconds)));
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
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

        using var client = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
            "AppleWebKit/537.36 Chrome/130.0.0.0 Safari/537.36"
        );

        var tasks = Endpoints
            .Select(endpoint => ProbeEndpointAsync(
                client,
                endpoint.Url,
                endpoint.ExpectedText,
                linked.Token
            ))
            .ToList();
        while (tasks.Count > 0)
        {
            var completed = await Task.WhenAny(tasks);
            tasks.Remove(completed);
            var result = await completed;
            if (result.Success)
            {
                linked.Cancel();
                stopwatch.Stop();
                return new InternetProbeResult(
                    true,
                    stopwatch.ElapsedMilliseconds,
                    result.Detail
                );
            }
        }

        stopwatch.Stop();
        return new InternetProbeResult(
            false,
            stopwatch.ElapsedMilliseconds,
            "所有外网探测点均不可达"
        );
    }

    private static async Task<(bool Success, string Detail)> ProbeEndpointAsync(
        HttpClient client,
        string url,
        string expectedText,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken
            );
            if (!response.IsSuccessStatusCode)
            {
                return (false, $"{url} HTTP {(int)response.StatusCode}");
            }
            if (url.Contains("generate_204", StringComparison.Ordinal))
            {
                return (true, $"{url} HTTP 204");
            }
            var text = await response.Content.ReadAsStringAsync(
                cancellationToken
            );
            if (!string.IsNullOrEmpty(expectedText) &&
                !text.Contains(expectedText, StringComparison.Ordinal))
            {
                return (false, $"{url} 返回内容异常");
            }
            return (true, $"{url} HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex)
        {
            return (false, $"{url} {ex.GetType().Name}");
        }
    }
}
