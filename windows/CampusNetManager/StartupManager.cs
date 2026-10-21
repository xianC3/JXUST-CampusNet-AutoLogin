using Microsoft.Win32;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Xml.Linq;

namespace CampusNetManager;

public static class StartupManager
{
    private const string RunKey =
        @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceKey =
        @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string StartupApprovedKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "CampusNetManager";
    private const string OldValueName = "CampusNetAutoLogin";
    private const string ScheduledTaskName = "CampusNetManagerAutoStart";
    private static readonly string StartupShortcut = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "CampusNetManager.lnk"
    );
    private static readonly string LegacyLauncherPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CampusNetAutoLogin",
        "startup.vbs"
    );
    private static readonly string StartupLogPath = Path.Combine(
        SettingsStore.DataDirectory,
        "startup-launch.log"
    );

    public static void SetEnabled(bool enabled)
    {
        try
        {
            using var runKey = Registry.CurrentUser.CreateSubKey(RunKey);
            using var runOnceKey = Registry.CurrentUser.CreateSubKey(RunOnceKey);
            using var approvedKey = Registry.CurrentUser.OpenSubKey(
                StartupApprovedKey,
                writable: true
            );

            // Remove every mechanism used by earlier builds. RUN entries are
            // capped by Windows on some systems, so only RUNONCE is used now.
            runKey?.DeleteValue(ValueName, false);
            runKey?.DeleteValue(OldValueName, false);
            approvedKey?.DeleteValue(ValueName, false);
            DeleteStartupShortcut();
            DeleteLegacyLauncher();

            if (enabled)
            {
                if (ScheduledTaskExists())
                {
                    runOnceKey?.DeleteValue(ValueName, false);
                }
                else
                {
                    runOnceKey?.SetValue(
                        ValueName,
                        $"\"{Application.ExecutablePath}\" --background --startup",
                        RegistryValueKind.String
                    );
                }
            }
            else
            {
                runOnceKey?.DeleteValue(ValueName, false);
                runOnceKey?.DeleteValue(OldValueName, false);
            }
        }
        catch
        {
        }
    }

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunOnceKey);
                var value = key?.GetValue(ValueName) as string;
                var runOnceConfigured =
                    !string.IsNullOrWhiteSpace(value) &&
                    value.Contains(
                        Application.ExecutablePath,
                        StringComparison.OrdinalIgnoreCase
                    );
                return runOnceConfigured || ScheduledTaskExists();
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool RunOnceConfigured
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunOnceKey);
                var value = key?.GetValue(ValueName) as string;
                return !string.IsNullOrWhiteSpace(value) &&
                       value.Contains(
                           Application.ExecutablePath,
                           StringComparison.OrdinalIgnoreCase
                       );
            }
            catch
            {
                return false;
            }
        }
    }

    public static bool ScheduledTaskExists()
    {
        return RunTaskCommand(
            "/Query",
            "/TN", ScheduledTaskName
        ) == 0;
    }

    public static bool TryInstallScheduledTask()
    {
        try
        {
            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrWhiteSpace(sid))
            {
                return false;
            }

            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            var document = new XDocument(
                new XDeclaration("1.0", "UTF-16", null),
                new XElement(
                    ns + "Task",
                    new XAttribute("version", "1.2"),
                    new XElement(
                        ns + "RegistrationInfo",
                        new XElement(ns + "Author", WindowsIdentity.GetCurrent().Name)
                    ),
                    new XElement(
                        ns + "Triggers",
                        new XElement(
                            ns + "LogonTrigger",
                            new XElement(ns + "Enabled", "true"),
                            new XElement(ns + "UserId", sid),
                            new XElement(ns + "Delay", "PT5S")
                        )
                    ),
                    new XElement(
                        ns + "Principals",
                        new XElement(
                            ns + "Principal",
                            new XAttribute("id", "Author"),
                            new XElement(ns + "UserId", sid),
                            new XElement(ns + "LogonType", "InteractiveToken"),
                            new XElement(ns + "RunLevel", "LeastPrivilege")
                        )
                    ),
                    new XElement(
                        ns + "Settings",
                        new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                        new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                        new XElement(ns + "StopIfGoingOnBatteries", "false"),
                        new XElement(ns + "AllowHardTerminate", "true"),
                        new XElement(ns + "StartWhenAvailable", "true"),
                        new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                        new XElement(ns + "IdleSettings",
                            new XElement(ns + "StopOnIdleEnd", "false"),
                            new XElement(ns + "RestartOnIdle", "false")
                        ),
                        new XElement(ns + "AllowStartOnDemand", "true"),
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "Hidden", "false"),
                        new XElement(ns + "RunOnlyIfIdle", "false"),
                        new XElement(ns + "WakeToRun", "false"),
                        new XElement(ns + "ExecutionTimeLimit", "PT0S")
                    ),
                    new XElement(
                        ns + "Actions",
                        new XAttribute("Context", "Author"),
                        new XElement(
                            ns + "Exec",
                            new XElement(ns + "Command", Application.ExecutablePath),
                            new XElement(ns + "Arguments", "--background --startup")
                        )
                    )
                )
            );

            var xmlPath = Path.Combine(
                Path.GetTempPath(),
                $"campusnet-task-{Guid.NewGuid():N}.xml"
            );
            try
            {
                using (var writer = new StreamWriter(xmlPath, false, Encoding.Unicode))
                {
                    document.Save(writer);
                }
                return RunTaskCommand(
                    "/Create",
                    "/TN", ScheduledTaskName,
                    "/XML", xmlPath,
                    "/F"
                ) == 0;
            }
            finally
            {
                try
                {
                    File.Delete(xmlPath);
                }
                catch
                {
                }
            }
        }
        catch
        {
            return false;
        }
    }

    public static void RemoveScheduledTask()
    {
        RunTaskCommand(
            "/Delete",
            "/TN", ScheduledTaskName,
            "/F"
        );
    }

    public static void RecordApplicationStart(string[] args)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StartupLogPath) ?? "");
            var line =
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} application-start " +
                $"arguments={string.Join(" ", args)}{Environment.NewLine}";
            File.AppendAllText(StartupLogPath, line, Encoding.UTF8);
        }
        catch
        {
        }
    }

    private static void DeleteStartupShortcut()
    {
        try
        {
            if (!File.Exists(StartupShortcut))
            {
                return;
            }

            File.SetAttributes(StartupShortcut, FileAttributes.Normal);
            File.Delete(StartupShortcut);
        }
        catch
        {
        }
    }

    private static void DeleteLegacyLauncher()
    {
        try
        {
            if (File.Exists(LegacyLauncherPath))
            {
                File.Delete(LegacyLauncherPath);
            }
        }
        catch
        {
        }
    }

    private static int RunTaskCommand(params string[] arguments)
    {
        try
        {
            var info = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments)
            {
                info.ArgumentList.Add(argument);
            }
            using var process = Process.Start(info);
            if (process == null)
            {
                return -1;
            }
            process.WaitForExit(15000);
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
