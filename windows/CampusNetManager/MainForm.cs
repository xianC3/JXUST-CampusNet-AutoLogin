using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Security.Cryptography;

namespace CampusNetManager;

public sealed class MainForm : Form
{
    private static readonly Color Brand = Color.FromArgb(13, 71, 161);
    private static readonly Color Page = Color.FromArgb(245, 247, 251);
    private static readonly Color TextPrimary = Color.FromArgb(24, 33, 47);
    private static readonly Color TextSecondary = Color.FromArgb(96, 112, 137);
    private static readonly Color Success = Color.FromArgb(27, 127, 75);

    private readonly AppSettings _settings;
    private readonly CampusLoginClient _client;
    private readonly bool _startHidden;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly NotifyIcon _tray;
    private readonly Label _statusTitle;
    private readonly Label _statusDetail;
    private readonly Label _wifiLabel;
    private readonly Label _listLabel;
    private readonly Label _diagnosticsLabel;
    private readonly CheckBox _autoCheck;
    private readonly Button _checkButton;
    private bool _checking;
    private bool _exitRequested;
    private string _lastStatus = "等待检查";

    public MainForm(bool startHidden)
    {
        _startHidden = startHidden;
        _settings = SettingsStore.Load();
        _client = new CampusLoginClient(_settings);

        Text = "江西理工大学校园网自动登录";
        try
        {
            var appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (appIcon != null)
            {
                Icon = appIcon;
            }
        }
        catch
        {
        }
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(760, 500);
        MinimumSize = new Size(700, 470);
        BackColor = Page;
        Font = new Font("Microsoft YaHei UI", 10f);
        AutoScroll = true;

        _statusTitle = CreateLabel("等待检查", 11.5f, FontStyle.Bold, TextPrimary);
        _statusDetail = CreateLabel("正在准备自动登录服务", 7.5f, FontStyle.Regular, TextSecondary);
        _wifiLabel = CreateLabel("当前 Wi-Fi：正在读取", 7.5f, FontStyle.Regular, TextPrimary);
        _listLabel = CreateLabel("校园网名单：正在读取", 7.5f, FontStyle.Regular, TextSecondary);
        _diagnosticsLabel = CreateLabel("", 7f, FontStyle.Regular, TextSecondary);
        _diagnosticsLabel.Height = 40;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Page
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82f));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Brand,
            Padding = new Padding(24, 10, 24, 8)
        };
        var title = CreateLabel("江西理工大学校园网自动登录", 14f, FontStyle.Bold, Color.White);
        title.Dock = DockStyle.Top;
        title.Height = 30;
        var subtitle = CreateLabel("CampusNet Manager · 只在校园 Wi-Fi 下工作", 7.5f, FontStyle.Regular, Color.FromArgb(218, 230, 250));
        subtitle.Dock = DockStyle.Top;
        subtitle.Height = 18;
        header.Controls.Add(subtitle);
        header.Controls.Add(title);
        root.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(18, 12, 18, 12),
            BackColor = Page
        };
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 74f));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 8f));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 66f));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 8f));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 36f));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var statusCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16, 6, 16, 5) };
        var statusLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _statusTitle.Dock = DockStyle.Fill;
        _statusDetail.Dock = DockStyle.Fill;
        statusLayout.Controls.Add(_statusTitle, 0, 0);
        statusLayout.Controls.Add(_statusDetail, 0, 1);
        statusCard.Controls.Add(statusLayout);
        content.Controls.Add(statusCard, 0, 0);

        var infoCard = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16, 5, 16, 5) };
        var infoLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        infoLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _wifiLabel.Dock = DockStyle.Fill;
        _listLabel.Dock = DockStyle.Fill;
        infoLayout.Controls.Add(_wifiLabel, 0, 0);
        infoLayout.Controls.Add(_listLabel, 0, 1);
        infoCard.Controls.Add(infoLayout);
        content.Controls.Add(infoCard, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0)
        };
        _checkButton = CreateButton("立即登录", Brand, Color.White);
        _checkButton.Click += async (_, _) => await CheckNowAsync();
        var addButton = CreateButton("添加当前 Wi-Fi", Color.White, TextPrimary, true);
        addButton.Click += (_, _) => AddCurrentSsid();
        var manageButton = CreateButton("管理名单", Color.White, TextPrimary, true);
        manageButton.Click += (_, _) => ManageSsids();
        var accountButton = CreateButton("账号设置", Color.White, TextPrimary, true);
        accountButton.Click += (_, _) => ShowAccountDialog(false);
        buttons.Controls.AddRange(new Control[] { _checkButton, addButton, manageButton, accountButton });
        content.Controls.Add(buttons, 0, 4);

        _autoCheck = new CheckBox
        {
            Text = "登录 Windows 后自动在后台运行",
            Checked = _settings.AutoLoginEnabled,
            AutoSize = true,
            Font = new Font("Microsoft YaHei UI", 7.5f),
            ForeColor = TextPrimary,
            Padding = new Padding(3, 6, 0, 0)
        };
        _autoCheck.CheckedChanged += (_, _) =>
        {
            _settings.AutoLoginEnabled = _autoCheck.Checked;
            SettingsStore.Save(_settings);
            StartupManager.SetEnabled(_settings.AutoLoginEnabled && HasCredentials());
        };
        content.Controls.Add(_autoCheck, 0, 5);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        bottom.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));
        bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _diagnosticsLabel.Dock = DockStyle.Fill;
        bottom.Controls.Add(_diagnosticsLabel, 0, 0);
        var links = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight };
        var logLink = CreateLink("查看日志");
        logLink.Click += (_, _) => OpenLog();
        var folderLink = CreateLink("打开数据目录");
        folderLink.Click += (_, _) => OpenDataFolder();
        links.Controls.AddRange(new Control[] { logLink, folderLink });
        bottom.Controls.Add(links, 0, 1);
        bottom.SetColumnSpan(_diagnosticsLabel, 2);
        bottom.SetColumnSpan(links, 2);
        content.Controls.Add(bottom, 0, 6);

        root.Controls.Add(content, 0, 1);
        Controls.Add(root);

        _timer = new System.Windows.Forms.Timer { Interval = 5 * 60 * 1000 };
        _timer.Tick += async (_, _) =>
        {
            if (_settings.AutoLoginEnabled)
            {
                await CheckNowAsync();
            }
        };
        _timer.Start();
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
        NetworkChange.NetworkAddressChanged += OnNetworkAddressChanged;

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开管理界面", null, (_, _) => ShowFromTray());
        menu.Items.Add("立即检查", null, async (_, _) => await CheckNowAsync());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());
        _tray = new NotifyIcon
        {
            Icon = Icon ?? SystemIcons.Application,
            Text = "江西理工大学校园网自动登录",
            Visible = true,
            ContextMenuStrip = menu
        };
        _tray.DoubleClick += (_, _) => ShowFromTray();

        Shown += async (_, _) => await OnShownAsync();
        FormClosing += OnFormClosing;
        RefreshUi();
    }

    private async Task OnShownAsync()
    {
        if (!HasCredentials())
        {
            if (!ShowAccountDialog(true))
            {
                _exitRequested = true;
                Close();
                return;
            }
        }

        if (_settings.AutoLoginEnabled && HasCredentials())
        {
            StartupManager.SetEnabled(true);
            SettingsStore.Log(
                StartupManager.IsEnabled
                    ? "STARTUP auto-start configured"
                    : "STARTUP auto-start configuration failed"
            );
        }

        if (_startHidden && HasCredentials())
        {
            HideToTray();
        }
        else
        {
            WindowState = FormWindowState.Normal;
            ShowInTaskbar = true;
            Activate();
        }

        await CheckNowAsync();
    }

    private async Task CheckNowAsync()
    {
        if (_checking)
        {
            return;
        }
        if (!HasCredentials())
        {
            SetStatus("尚未设置账号", "请先填写校园网账号和密码", TextSecondary);
            ShowAccountDialog(true);
            return;
        }

        var password = CredentialStore.Load();
        if (string.IsNullOrEmpty(password))
        {
            SetStatus("账号凭据不可用", "请重新设置密码", Color.FromArgb(180, 45, 45));
            ShowAccountDialog(true);
            return;
        }

        _checking = true;
        _checkButton.Enabled = false;
        SetStatus("正在检查...", "正在识别当前网络并检查校园网状态", Brand);
        try
        {
            var result = await _client.CheckAndLoginAsync(password);
            _lastStatus = result.Message;
            SettingsStore.Log(
                $"{(result.Success ? "OK" : (result.Retry ? "WAIT" : "ERROR"))} {result.Message}"
            );
            SetStatus(
                result.Success && !result.Skipped ? "校园网已连接" : "等待校园网",
                result.Message,
                result.Success ? Success : (result.Retry ? Brand : Color.FromArgb(180, 45, 45))
            );
            _tray.Text = "江西理工大学校园网自动登录 - " +
                         (result.Success && !result.Skipped ? "已连接" : "等待中");
        }
        catch (Exception ex)
        {
            _lastStatus = "检查失败";
            SettingsStore.Log($"ERROR {ex.GetType().Name}");
            SetStatus("检查失败", "无法连接门户，请稍后重试", Color.FromArgb(180, 45, 45));
        }
        finally
        {
            _checking = false;
            _checkButton.Enabled = true;
            RefreshUi();
        }
    }

    private bool ShowAccountDialog(bool firstRun)
    {
        using var dialog = new AccountDialog(
            _settings.Account,
            _settings.ProviderSuffix,
            firstRun
        );
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        _settings.Account = dialog.Account;
        _settings.ProviderSuffix = dialog.ProviderSuffix;
        if (!string.IsNullOrEmpty(dialog.Password))
        {
            CredentialStore.Save(dialog.Password);
        }
        SettingsStore.Save(_settings);
        StartupManager.SetEnabled(_settings.AutoLoginEnabled);
        RefreshUi();
        return true;
    }

    private void AddCurrentSsid()
    {
        var ssid = NetworkService.GetCurrentSsid();
        if (string.IsNullOrWhiteSpace(ssid))
        {
            MessageBox.Show(
                this,
                "当前系统暂时没有提供 Wi-Fi 名称。\n请保持连接，稍后重试；也可以在 Wi-Fi 设置中确认当前网络。",
                "暂时无法读取 Wi-Fi",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
            return;
        }

        if (_settings.CampusSsids.Any(x => string.Equals(x, ssid, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"「{ssid}」已经在校园网名单中。", "无需重复添加", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _settings.CampusSsids.Add(ssid);
        SettingsStore.Save(_settings);
        RefreshUi();
        MessageBox.Show(this, $"已添加校园网：{ssid}", "添加成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ManageSsids()
    {
        using var dialog = new SsidManagerForm(_settings.CampusSsids);
        dialog.ShowDialog(this);
        if (dialog.Changed)
        {
            _settings.CampusSsids = dialog.Ssids.ToList();
            SettingsStore.Save(_settings);
            RefreshUi();
            if (_settings.AutoLoginEnabled)
            {
                _ = CheckNowAsync();
            }
        }
    }

    private void RefreshUi()
    {
        var ssid = NetworkService.GetCurrentSsid();
        _wifiLabel.Text = string.IsNullOrWhiteSpace(ssid)
            ? "当前 Wi-Fi：暂时无法读取名称"
            : $"当前 Wi-Fi：{ssid}";
        _listLabel.Text = _settings.CampusSsids.Count == 0
            ? "校园网名单：（空）"
            : "校园网名单：" + string.Join("、", _settings.CampusSsids);
        _autoCheck.Checked = _settings.AutoLoginEnabled;

        var location = NetworkService.IsOnWifi() ? "Wi-Fi 已连接" : "未检测到 Wi-Fi";
        var provider = ProviderCatalog.FindBySuffix(_settings.ProviderSuffix).Name;
        var account = HasCredentials() ? $"账号已设置（{provider}）" : "账号未设置";
        _diagnosticsLabel.Text =
            $"兼容性检查：{location}｜{account}｜" +
            $"自动检查每 5 分钟一次｜开机启动 " +
            (_settings.AutoLoginEnabled ? "已开启" : "已关闭");
    }

    private bool HasCredentials()
    {
        return !string.IsNullOrWhiteSpace(_settings.Account) && CredentialStore.Exists;
    }

    private void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (_settings.AutoLoginEnabled && IsHandleCreated)
        {
            BeginInvoke(async () => await CheckNowAsync());
        }
    }

    private void OnNetworkAddressChanged(object? sender, EventArgs e)
    {
        if (_settings.AutoLoginEnabled && IsHandleCreated)
        {
            BeginInvoke(async () => await CheckNowAsync());
        }
    }

    private void HideToTray()
    {
        WindowState = FormWindowState.Minimized;
        ShowInTaskbar = false;
        Hide();
    }

    internal void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        ShowInTaskbar = true;
        Activate();
    }

    private void ExitApplication()
    {
        _exitRequested = true;
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        Close();
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_exitRequested && _settings.AutoLoginEnabled)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private void OpenLog()
    {
        if (File.Exists(SettingsStore.LogPath))
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{SettingsStore.LogPath}\"") { UseShellExecute = true });
        }
        else
        {
            MessageBox.Show(this, "还没有日志记录。", "日志", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }

    private void OpenDataFolder()
    {
        Directory.CreateDirectory(SettingsStore.DataDirectory);
        Process.Start(new ProcessStartInfo(SettingsStore.DataDirectory) { UseShellExecute = true });
    }

    private void SetStatus(string title, string detail, Color color)
    {
        _statusTitle.Text = title;
        _statusTitle.ForeColor = color;
        _statusDetail.Text = detail;
    }

    private static Label CreateLabel(string text, float size, FontStyle style, Color color)
    {
        return new Label
        {
            Text = text,
            AutoSize = false,
            ForeColor = color,
            Font = new Font("Microsoft YaHei UI", size, style),
            TextAlign = ContentAlignment.MiddleLeft
        };
    }

    private static Button CreateButton(string text, Color back, Color fore, bool bordered = false)
    {
        var button = new Button
        {
            Text = text,
            Width = 142,
            Height = 36,
            Font = new Font("Microsoft YaHei UI", 8f),
            BackColor = back,
            ForeColor = fore,
            FlatStyle = bordered ? FlatStyle.Flat : FlatStyle.Standard,
            Margin = new Padding(0, 0, 10, 0)
        };
        if (bordered)
        {
            button.FlatAppearance.BorderColor = Color.FromArgb(200, 212, 230);
        }
        return button;
    }

    private static LinkLabel CreateLink(string text)
    {
        return new LinkLabel
        {
            Text = text,
            AutoSize = true,
            LinkColor = Brand,
            Margin = new Padding(0, 0, 24, 0)
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            NetworkCycleMonitor.Stop();
            NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
            NetworkChange.NetworkAddressChanged -= OnNetworkAddressChanged;
            _timer.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
