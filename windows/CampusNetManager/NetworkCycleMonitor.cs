using System.Text;
using System.Text.Json;

namespace CampusNetManager;

public static class NetworkCycleMonitor
{
    private static readonly object Sync = new();
    private static CancellationTokenSource? _stop;
    private static Task? _loop;
    private static bool _running;

    public static bool IsRunning
    {
        get
        {
            lock (Sync)
            {
                return _running;
            }
        }
    }

    public static bool Start(int targetCycles = 10, int maxWaitSeconds = 90)
    {
        lock (Sync)
        {
            if (_running)
            {
                return false;
            }
            _running = true;
            _stop = new CancellationTokenSource();
            _loop = Task.Run(() => RunLoopAsync(
                Math.Max(1, targetCycles),
                Math.Max(20, maxWaitSeconds),
                _stop.Token
            ));
            return true;
        }
    }

    public static void Stop()
    {
        lock (Sync)
        {
            _stop?.Cancel();
        }
    }

    private static async Task RunLoopAsync(
        int targetCycles,
        int maxWaitSeconds,
        CancellationToken cancellationToken)
    {
        var settings = SettingsStore.Load();
        var password = CredentialStore.Load() ?? "";
        var records = new List<CycleRecord>();
        var startedAt = DateTime.Now;
        var wasConnected = NetworkService.IsOnWifi();
        var armed = false;
        DateTime? offAt = null;
        ActiveCycle? active = null;

        WriteLive(records, active, targetCycles, "等待关闭 Wi-Fi");
        SettingsStore.Log(
            $"CYCLE-TEST 开始：目标 {targetCycles} 次，单次最长 {maxWaitSeconds} 秒"
        );

        try
        {
            while (!cancellationToken.IsCancellationRequested &&
                   records.Count < targetCycles)
            {
                var now = DateTime.Now;
                var connected = NetworkService.IsOnWifi();
                if (!connected)
                {
                    if (active != null)
                    {
                        Finish(
                            records,
                            active,
                            false,
                            "等待期间 Wi-Fi 再次断开"
                        );
                        active = null;
                    }
                    if (wasConnected || offAt == null)
                    {
                        offAt = now;
                    }
                    armed = true;
                    WriteLive(
                        records,
                        active,
                        targetCycles,
                        $"已检测断开，等待第 {records.Count + 1} 次连接"
                    );
                }
                else
                {
                    if (!wasConnected && armed &&
                        records.Count < targetCycles)
                    {
                        active = new ActiveCycle
                        {
                            Number = records.Count + 1,
                            OffAt = offAt,
                            OnAt = now
                        };
                        armed = false;
                        SettingsStore.Log(
                            $"CYCLE-TEST 第 {active.Number} 次连接已检测"
                        );
                    }

                    if (active != null)
                    {
                        await SampleCycleAsync(
                            active,
                            settings,
                            password,
                            cancellationToken
                        );
                        if (active.Completed)
                        {
                            Finish(
                                records,
                                active,
                                active.PortalAndInternet,
                                active.Message
                            );
                            active = null;
                        }
                        else if ((now - active.OnAt).TotalSeconds >
                                 maxWaitSeconds)
                        {
                            Finish(
                                records,
                                active,
                                false,
                                active.Message.Length > 0
                                    ? active.Message + "；等待超时"
                                    : "等待校园认证和外网超时"
                            );
                            active = null;
                        }
                    }

                    WriteLive(
                        records,
                        active,
                        targetCycles,
                        active == null
                            ? $"已完成 {records.Count}/{targetCycles}"
                            : active.Message
                    );
                }

                wasConnected = connected;
                await Task.Delay(1500, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            SettingsStore.Log("CYCLE-TEST 已由用户停止");
        }
        finally
        {
            var finishedAt = DateTime.Now;
            WriteReport(records, targetCycles, startedAt, finishedAt);
            WriteLive(
                records,
                null,
                targetCycles,
                $"测试结束：成功 {records.Count(item => item.PortalAndInternet)}" +
                $"/{records.Count}",
                running: false
            );
            SettingsStore.Log(
                $"CYCLE-TEST 结束：成功 {records.Count(item => item.PortalAndInternet)}" +
                $"/{records.Count}，目标 {targetCycles}"
            );
            lock (Sync)
            {
                _running = false;
            }
        }
    }

    private static async Task SampleCycleAsync(
        ActiveCycle active,
        AppSettings settings,
        string password,
        CancellationToken cancellationToken)
    {
        var ssid = NetworkService.GetCurrentSsid();
        var ip = NetworkService.GetWifiIpv4();
        var mac = NetworkService.GetWifiMacAddress();
        active.Observations++;
        active.Ssid = ssid;
        active.Ip = ip;
        active.Mac = mac;

        if (string.IsNullOrWhiteSpace(ssid))
        {
            active.Message = "Wi-Fi 已连接，但暂未读到 SSID";
            return;
        }
        if (!settings.CampusSsids.Any(
                item => string.Equals(
                    item,
                    ssid,
                    StringComparison.OrdinalIgnoreCase)))
        {
            active.CampusSsid = false;
            active.Message = $"当前连接“{ssid}”，不是校园网名单";
            return;
        }

        active.CampusSsid = true;
        if (string.IsNullOrEmpty(password))
        {
            active.Message = "校园网已连接，但无法读取密码凭据";
            return;
        }

        var result = await new CampusLoginClient(
            settings,
            bindToWifi: true,
            log: _ => { }
        ).CheckAndLoginAsync(
            password,
            cancellationToken,
            loginIfOffline: false,
            allowWifiSwitch: false
        );
        active.PortalAndInternet = result.Success;
        active.Message = result.Message;
        active.Completed = result.Success;
    }

    private static void Finish(
        List<CycleRecord> records,
        ActiveCycle active,
        bool success,
        string message)
    {
        var completedAt = DateTime.Now;
        records.Add(new CycleRecord
        {
            Cycle = active.Number,
            OffAt = active.OffAt,
            OnAt = active.OnAt,
            CompletedAt = completedAt,
            Ssid = active.Ssid,
            Ip = active.Ip,
            Mac = active.Mac,
            CampusSsid = active.CampusSsid,
            PortalAndInternet = success,
            Seconds = Math.Round(
                (completedAt - active.OnAt).TotalSeconds,
                2
            ),
            Message = message,
            Observations = active.Observations
        });
        SettingsStore.Log(
            $"CYCLE-TEST 第 {active.Number} 次" +
            $"{(success ? "通过" : "失败")}：" +
            $"{Math.Round((completedAt - active.OnAt).TotalSeconds, 2)} 秒，" +
            message
        );
    }

    private static void WriteLive(
        List<CycleRecord> records,
        ActiveCycle? active,
        int targetCycles,
        string message,
        bool running = true)
    {
        try
        {
            var payload = new
            {
                Running = running,
                Target = targetCycles,
                Completed = records.Count,
                ActiveCycle = active?.Number,
                UpdatedAt = DateTime.Now,
                Message = message,
                Records = records
            };
            File.WriteAllText(
                Path.Combine(
                    SettingsStore.DataDirectory,
                    "wifi-cycle-live.json"
                ),
                JsonSerializer.Serialize(
                    payload,
                    new JsonSerializerOptions { WriteIndented = true }
                ),
                Encoding.UTF8
            );
        }
        catch
        {
        }
    }

    private static void WriteReport(
        List<CycleRecord> records,
        int targetCycles,
        DateTime startedAt,
        DateTime finishedAt)
    {
        try
        {
            var successful = records.Count(item => item.PortalAndInternet);
            var report = new
            {
                Version = "1.4.1",
                StartedAt = startedAt,
                FinishedAt = finishedAt,
                TargetCycles = targetCycles,
                CompletedCycles = records.Count,
                SuccessfulCycles = successful,
                FailedCycles = records.Count - successful,
                AverageSeconds = records.Count == 0
                    ? 0
                    : Math.Round(records.Average(item => item.Seconds), 2),
                MaxSeconds = records.Count == 0
                    ? 0
                    : records.Max(item => item.Seconds),
                Records = records
            };
            var directory = SettingsStore.DataDirectory;
            File.WriteAllText(
                Path.Combine(directory, "wifi-cycle-report.json"),
                JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true }
                ),
                Encoding.UTF8
            );

            var text = new StringBuilder()
                .AppendLine("校园网 Wi-Fi 开关循环测试")
                .AppendLine($"开始：{startedAt:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"结束：{finishedAt:yyyy-MM-dd HH:mm:ss}")
                .AppendLine($"目标：{targetCycles} 次")
                .AppendLine($"完成：{records.Count} 次")
                .AppendLine($"成功：{successful} 次")
                .AppendLine($"失败：{records.Count - successful} 次")
                .AppendLine($"平均：{(records.Count == 0 ? 0 : records.Average(item => item.Seconds)):F2} 秒")
                .AppendLine($"最长：{(records.Count == 0 ? 0 : records.Max(item => item.Seconds)):F2} 秒")
                .AppendLine();
            foreach (var record in records)
            {
                text.AppendLine(
                    $"第 {record.Cycle} 次：{(record.PortalAndInternet ? "通过" : "失败")}，" +
                    $"{record.Seconds:F2} 秒，SSID={record.Ssid}，" +
                    $"IP={record.Ip}，{record.Message}"
                );
            }
            File.WriteAllText(
                Path.Combine(directory, "wifi-cycle-report.txt"),
                text.ToString(),
                Encoding.UTF8
            );
        }
        catch
        {
        }
    }

    private sealed class ActiveCycle
    {
        public int Number { get; init; }
        public DateTime? OffAt { get; init; }
        public DateTime OnAt { get; init; }
        public int Observations { get; set; }
        public string? Ssid { get; set; }
        public string? Ip { get; set; }
        public string? Mac { get; set; }
        public bool CampusSsid { get; set; }
        public bool PortalAndInternet { get; set; }
        public bool Completed { get; set; }
        public string Message { get; set; } = "";
    }

    private sealed class CycleRecord
    {
        public int Cycle { get; init; }
        public DateTime? OffAt { get; init; }
        public DateTime OnAt { get; init; }
        public DateTime CompletedAt { get; init; }
        public string? Ssid { get; init; }
        public string? Ip { get; init; }
        public string? Mac { get; init; }
        public bool CampusSsid { get; init; }
        public bool PortalAndInternet { get; init; }
        public double Seconds { get; init; }
        public string Message { get; init; } = "";
        public int Observations { get; init; }
    }
}
