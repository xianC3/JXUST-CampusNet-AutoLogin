namespace CampusNetManager;

static class Program
{
    private const string MutexName =
        @"Local\CampusNetManager.SingleInstance";
    private const string ShowEventName =
        @"Local\CampusNetManager.ShowWindow";
    private const string CycleTestEventName =
        @"Local\CampusNetManager.StartCycleTest";

    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Any(arg => arg.Equals("--install-startup-task", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode =
                StartupManager.TryInstallScheduledTask() ? 0 : 1;
            return;
        }
        if (args.Any(arg => arg.Equals("--uninstall-startup", StringComparison.OrdinalIgnoreCase)))
        {
            StartupManager.SetEnabled(false);
            StartupManager.RemoveScheduledTask();
            return;
        }
        if (args.Any(arg => arg.Equals("--self-test", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = SelfTestRunner.Run();
            return;
        }

        using var mutex = new Mutex(true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            try
            {
                if (args.Any(
                        arg => arg.Equals(
                            "--cycle-test",
                            StringComparison.OrdinalIgnoreCase)))
                {
                    using var cycleEvent =
                        EventWaitHandle.OpenExisting(CycleTestEventName);
                    cycleEvent.Set();
                }
                else
                {
                    using var showEvent =
                        EventWaitHandle.OpenExisting(ShowEventName);
                    showEvent.Set();
                }
            }
            catch
            {
            }
            return;
        }

        StartupManager.RecordApplicationStart(args);
        var startupLaunch = args.Any(
            arg => arg.Equals("--startup", StringComparison.OrdinalIgnoreCase)
        );
        if (startupLaunch)
        {
            if (!SettingsStore.Load().AutoLoginEnabled)
            {
                StartupManager.RecordApplicationStart(
                    args.Append("auto-login-disabled").ToArray()
                );
                return;
            }
        }
        var startHidden = args.Any(
            arg => arg.Equals("--background", StringComparison.OrdinalIgnoreCase)
        );
        using var showHandle = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            ShowEventName
        );
        using var cycleHandle = new EventWaitHandle(
            false,
            EventResetMode.AutoReset,
            CycleTestEventName
        );
        var form = new MainForm(startHidden);
        if (args.Any(
                arg => arg.Equals(
                    "--cycle-test",
                    StringComparison.OrdinalIgnoreCase)))
        {
            NetworkCycleMonitor.Start(targetCycles: 10, maxWaitSeconds: 90);
        }
        var showThread = new Thread(() =>
        {
            while (true)
            {
                var signaled = WaitHandle.WaitAny(
                    new WaitHandle[] { showHandle, cycleHandle }
                );
                if (form.IsDisposed)
                {
                    break;
                }
                try
                {
                    if (signaled == 1)
                    {
                        NetworkCycleMonitor.Start(
                            targetCycles: 10,
                            maxWaitSeconds: 90
                        );
                    }
                    else
                    {
                        form.BeginInvoke(form.ShowFromTray);
                    }
                }
                catch
                {
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "CampusNetManager.ShowWindow"
        };
        showThread.Start();
        Application.Run(form);
    }
}
