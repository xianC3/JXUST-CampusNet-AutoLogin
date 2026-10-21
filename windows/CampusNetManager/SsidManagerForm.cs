namespace CampusNetManager;

public sealed class SsidManagerForm : Form
{
    private readonly ListBox _list;
    private readonly List<string> _ssids;

    public IReadOnlyList<string> Ssids => _ssids;
    public bool Changed { get; private set; }

    public SsidManagerForm(IEnumerable<string> ssids)
    {
        _ssids = ssids
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Text = "管理校园网名单";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 390);
        Font = new Font("Microsoft YaHei UI", 10f);
        BackColor = Color.FromArgb(245, 247, 251);

        var title = new Label
        {
            Text = "校园网 Wi-Fi 名单",
            Font = new Font("Microsoft YaHei UI", 17f, FontStyle.Bold),
            ForeColor = Color.FromArgb(24, 33, 47),
            AutoSize = true,
            Location = new Point(28, 24)
        };
        var hint = new Label
        {
            Text = "只有名单中的网络会自动登录。删除后可重新添加。",
            ForeColor = Color.FromArgb(96, 112, 137),
            AutoSize = true,
            Location = new Point(30, 66)
        };
        _list = new ListBox
        {
            Location = new Point(30, 100),
            Size = new Size(460, 210),
            Font = new Font("Microsoft YaHei UI", 11f),
            SelectionMode = SelectionMode.One
        };
        _list.Items.AddRange(_ssids.Cast<object>().ToArray());

        var remove = new Button
        {
            Text = "删除选中网络",
            Location = new Point(30, 330),
            Size = new Size(150, 36)
        };
        var close = new Button
        {
            Text = "关闭",
            DialogResult = DialogResult.OK,
            Location = new Point(390, 330),
            Size = new Size(100, 36)
        };
        remove.Click += (_, _) => RemoveSelected();
        AcceptButton = close;
        CancelButton = close;

        Controls.AddRange(new Control[] { title, hint, _list, remove, close });
    }

    private void RemoveSelected()
    {
        if (_list.SelectedItem is not string selected)
        {
            MessageBox.Show(this, "请先选择一个网络。", "未选择网络", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(
                this,
                $"确定不再自动登录「{selected}」吗？",
                "确认删除",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _ssids.RemoveAll(x => string.Equals(x, selected, StringComparison.OrdinalIgnoreCase));
        _list.Items.Remove(selected);
        Changed = true;
    }
}
