namespace CampusNetManager;

public sealed class AccountDialog : Form
{
    private readonly TextBox _account;
    private readonly ComboBox _provider;
    private readonly TextBox _password;
    private readonly bool _firstRun;

    public string Account { get; private set; } = "";
    public string ProviderSuffix { get; private set; } = "";
    public string Password => _password.Text;

    public AccountDialog(string account, string providerSuffix, bool firstRun)
    {
        _firstRun = firstRun;
        var parsed = ProviderCatalog.Parse(account, providerSuffix);
        Account = account;
        ProviderSuffix = parsed.Suffix;
        Text = "校园网账号设置";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 370);
        Font = new Font("Microsoft YaHei UI", 10f);
        BackColor = Color.FromArgb(245, 247, 251);

        var title = new Label
        {
            Text = firstRun ? "首次设置校园网账号" : "修改校园网账号",
            Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 33, 47),
            AutoSize = true,
            Location = new Point(28, 24)
        };
        var hint = new Label
        {
            Text = "请选择校园网接入运营商，账号栏只填数字/字母账号。\n程序会自动拼接后缀，例如 联通 → @unicomyt。",
            ForeColor = Color.FromArgb(96, 112, 137),
            AutoSize = true,
            Location = new Point(30, 66)
        };
        var accountLabel = new Label
        {
            Text = "账号（不含运营商后缀）",
            ForeColor = Color.FromArgb(96, 112, 137),
            AutoSize = true,
            Location = new Point(30, 116)
        };
        _account = new TextBox
        {
            Text = parsed.LocalAccount,
            Location = new Point(30, 139),
            Width = 440,
            Font = new Font("Microsoft YaHei UI", 11f)
        };
        var providerLabel = new Label
        {
            Text = "运营商",
            ForeColor = Color.FromArgb(96, 112, 137),
            AutoSize = true,
            Location = new Point(30, 180)
        };
        _provider = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new Point(30, 203),
            Width = 240,
            Font = new Font("Microsoft YaHei UI", 11f)
        };
        _provider.Items.AddRange(ProviderCatalog.All.Select(item => item.Name).Cast<object>().ToArray());
        _provider.SelectedIndex = ProviderCatalog.All.ToList().FindIndex(
            item => string.Equals(item.Suffix, parsed.Suffix, StringComparison.OrdinalIgnoreCase)
        );

        var passwordLabel = new Label
        {
            Text = fileHasPassword ? "密码（留空保留现有密码）" : "密码",
            ForeColor = Color.FromArgb(96, 112, 137),
            AutoSize = true,
            Location = new Point(30, 244)
        };
        _password = new TextBox
        {
            Location = new Point(30, 267),
            Width = 440,
            UseSystemPasswordChar = true,
            Font = new Font("Microsoft YaHei UI", 11f),
            PlaceholderText = fileHasPassword ? "留空保留现有密码" : "请输入校园网密码"
        };

        var save = new Button
        {
            Text = "保存",
            DialogResult = DialogResult.OK,
            Location = new Point(280, 320),
            Width = 90,
            Height = 34
        };
        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Location = new Point(380, 320),
            Width = 90,
            Height = 34
        };
        AcceptButton = save;
        CancelButton = cancel;
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_account.Text))
            {
                MessageBox.Show(this, "请输入校园网账号。", "账号不能为空", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (_provider.SelectedIndex < 0)
            {
                MessageBox.Show(this, "请选择校园网运营商。", "运营商不能为空", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }
            if (_firstRun && string.IsNullOrEmpty(Password))
            {
                MessageBox.Show(this, "请输入校园网密码。", "密码不能为空", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
            }
            ProviderSuffix = ProviderCatalog.All[_provider.SelectedIndex].Suffix;
            Account = ProviderCatalog.Build(_account.Text, ProviderSuffix);
        };

        Controls.AddRange(new Control[]
        {
            title, hint, accountLabel, _account, providerLabel, _provider,
            passwordLabel, _password, save, cancel
        });
    }

    private bool fileHasPassword => CredentialStore.Exists;
}
