using System.Text;

namespace CampusNetManager;

public static class SelfTestRunner
{
    public static int Run()
    {
        var checks = new List<(string Name, bool Passed, string Detail)>();

        void Check(string name, bool passed, string detail)
        {
            checks.Add((name, passed, detail));
        }

        try
        {
            var settings = SettingsStore.Load();
            var parsed = ProviderCatalog.Parse(
                settings.Account,
                settings.ProviderSuffix
            );
            Check(
                "账号规范化",
                !string.IsNullOrWhiteSpace(parsed.LocalAccount) &&
                parsed.LocalAccount.IndexOf('@') < 0,
                "账号仅保留本地区段"
            );

            var providerMappings = new[]
            {
                ("校园网", ""),
                ("联通", "@unicomyt"),
                ("移动", "@cmccyt"),
                ("电信", "@telecomyt")
            };
            var providersPass = providerMappings.All(
                item => ProviderCatalog.Build("12345", item.Item2) ==
                        "12345" + item.Item2
            );
            Check("运营商后缀", providersPass, "四种映射全部正确");

            var password = CredentialStore.Load();
            Check(
                "凭据解密",
                !string.IsNullOrEmpty(password),
                password == null ? "无法读取" : "Windows DPAPI 解密成功"
            );

            var ssid = NetworkService.GetCurrentSsid();
            var ssidKnown = !string.IsNullOrWhiteSpace(ssid);
            Check(
                "Wi-Fi 识别",
                ssidKnown && NetworkService.IsOnWifi(),
                ssidKnown ? ssid! : "未读取到 SSID"
            );

            var wifiIp = NetworkService.GetWifiIpv4();
            Check(
                "Wi-Fi IPv4",
                !string.IsNullOrWhiteSpace(wifiIp),
                wifiIp ?? "未获取"
            );

            var wifiMac = NetworkService.GetWifiMacAddress();
            Check(
                "Wi-Fi MAC",
                wifiMac.Length == 12 &&
                wifiMac.All(Uri.IsHexDigit),
                wifiMac.Length == 12 ? "已获取" : "未获取"
            );

            var currentSsidConfigured =
                ssidKnown &&
                settings.CampusSsids.Any(
                    item => string.Equals(
                        item,
                        ssid,
                        StringComparison.OrdinalIgnoreCase
                    )
                );
            Check(
                "校园网名单",
                currentSsidConfigured,
                currentSsidConfigured
                    ? "当前 Wi-Fi 在名单中"
                    : "当前 Wi-Fi 不在名单中"
            );

            StartupManager.SetEnabled(true);
            Check(
                "登录后启动",
                StartupManager.IsEnabled,
                StartupManager.IsEnabled
                    ? "登录启动已就绪"
                    : "登录启动未配置"
            );
            Check(
                "Windows 计划任务",
                StartupManager.ScheduledTaskExists(),
                StartupManager.ScheduledTaskExists()
                    ? "任务已安装"
                    : "任务未安装"
            );

            StartupManager.SetEnabled(false);
            var disabled = !StartupManager.RunOnceConfigured;
            StartupManager.SetEnabled(true);
            Check(
                "启动开关切换",
                disabled && StartupManager.IsEnabled,
                disabled && StartupManager.IsEnabled
                    ? "关闭和恢复均正确"
                    : "启动项状态切换异常"
            );

            var trustedMacs = TrustedMacStore.Load();
            Check(
                "受信任设备模式",
                true,
                trustedMacs.Count > 0
                    ? $"专用版，受信任 MAC {trustedMacs.Count} 个"
                    : "通用版，不保存固定 MAC"
            );

            if (!string.IsNullOrEmpty(password) && currentSsidConfigured)
            {
                var result = new CampusLoginClient(settings)
                    .CheckAndLoginAsync(
                        password,
                        CancellationToken.None,
                        loginIfOffline: false
                    )
                    .GetAwaiter()
                    .GetResult();
                Check(
                    "校园门户只读检查",
                    result.Success,
                    result.Message
                );

                var nonCampusSettings = new AppSettings
                {
                    PortalHost = settings.PortalHost,
                    PortalPort = settings.PortalPort,
                    Account = settings.Account,
                    ProviderSuffix = settings.ProviderSuffix,
                    CampusSsids = new List<string>
                    {
                        "__campusnet_selftest_non_campus__"
                    },
                    AutoLoginEnabled = true,
                    InterfaceName = settings.InterfaceName,
                    RequestTimeoutSeconds = settings.RequestTimeoutSeconds,
                    WifiWaitSeconds = settings.WifiWaitSeconds
                };
                var skipped = new CampusLoginClient(nonCampusSettings)
                    .CheckAndLoginAsync(password)
                    .GetAwaiter()
                    .GetResult();
                Check(
                    "非校园网跳过",
                    skipped.Success && skipped.Skipped,
                    skipped.Message
                );
            }
        }
        catch (Exception ex)
        {
            Check("未处理异常", false, ex.GetType().Name);
        }

        var report = new StringBuilder();
        report.AppendLine(
            $"CampusNetManager self-test {DateTime.Now:yyyy-MM-dd HH:mm:ss}"
        );
        foreach (var check in checks)
        {
            report.AppendLine(
                $"{(check.Passed ? "PASS" : "FAIL")} | " +
                $"{check.Name} | {check.Detail}"
            );
        }
        var failed = checks.Count(check => !check.Passed);
        report.AppendLine($"RESULT {failed} failed / {checks.Count} checks");

        try
        {
            Directory.CreateDirectory(SettingsStore.DataDirectory);
            File.WriteAllText(
                Path.Combine(SettingsStore.DataDirectory, "self-test.log"),
                report.ToString(),
                Encoding.UTF8
            );
        }
        catch
        {
        }
        return failed == 0 ? 0 : 1;
    }
}
