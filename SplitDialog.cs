using System.Drawing;
using System.Windows.Forms;

namespace AgentHub;

internal sealed class SplitDialog : Form
{
    private readonly List<CheckBox> _boxes = new();

    public List<string> SelectedKeys { get; } = new();

    public SplitDialog(List<(string Key, string Title)> agents, IEnumerable<string> preselect)
    {
        Text = "选择要同屏显示的 Agent";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(320, 60 + agents.Count * 34 + 50);
        BackColor = Color.FromArgb(28, 28, 32);
        ForeColor = Color.White;
        Font = new Font("Microsoft YaHei UI", 9.5f);

        var y = 18;
        foreach (var a in agents)
        {
            var cb = new CheckBox
            {
                Text = a.Title,
                Tag = a.Key,
                Left = 24,
                Top = y,
                Width = 260,
                ForeColor = Color.White,
                Checked = preselect.Contains(a.Key)
            };
            Controls.Add(cb);
            _boxes.Add(cb);
            y += 34;
        }

        var ok = new Button
        {
            Text = "确定",
            DialogResult = DialogResult.OK,
            Left = ClientSize.Width - 170,
            Top = y + 10,
            Width = 70,
            BackColor = Color.FromArgb(60, 60, 70),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        ok.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 100);

        var cancel = new Button
        {
            Text = "取消",
            DialogResult = DialogResult.Cancel,
            Left = ClientSize.Width - 90,
            Top = y + 10,
            Width = 66,
            BackColor = Color.FromArgb(45, 45, 52),
            ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 90);

        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        ok.Click += (_, _) =>
        {
            SelectedKeys.Clear();
            foreach (var cb in _boxes)
                if (cb.Checked && cb.Tag is string key)
                    SelectedKeys.Add(key);
        };
    }
}
