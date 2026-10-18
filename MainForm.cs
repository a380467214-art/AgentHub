using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace AgentHub;

internal sealed class MainForm : Form
{
    private readonly string _configPath;
    private readonly Panel _content = new();
    private readonly Dictionary<string, AgentHost> _hosts = new();
    private readonly List<string> _keys = new();
    private readonly Dictionary<string, TabChip> _chipByKey = new();
    private readonly List<TabChip> _chips = new();
    private readonly Dictionary<string, Label> _tabHints = new();
    private readonly Label _statusLabel = new();
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly System.Windows.Forms.Timer _anim = new();
    private readonly System.Windows.Forms.Timer _focusTimer = new();
    private double _phase;

    private TableLayoutPanel _topBar = null!;
    private FlowLayoutPanel _chipFlow = null!;
    private FlowLayoutPanel _actionFlow = null!;
    private TableLayoutPanel _splitGrid = null!;
    private readonly Panel[] _cells = new Panel[4];
    private readonly Label[] _cellHint = new Label[4];
    private readonly List<string> _splitKeys = new();

    private int _selected;
    private bool _ready;
    private bool _layoutBusy;
    private string? _startupKey;
    private const int HotKeyBaseId = 0xA100;
    private int _hotKeyCount;

    private Button _btnOpen = null!;
    private Button _btnClose = null!;
    private Button _btnDetach = null!;
    private Button _btnRestart = null!;
    private Button _btnReattach = null!;
    private Button _btnSplit = null!;

    public MainForm()
    {
        _configPath = Path.Combine(AppContext.BaseDirectory, "agents.json");

        Text = "AgentHub — 多 Agent 工作台";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 620);
        Size = new Size(1440, 900);
        BackColor = Color.FromArgb(10, 10, 12);
        ForeColor = Color.White;
        KeyPreview = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        DoubleBuffered = true;

        _content.Dock = DockStyle.Fill;
        _content.BackColor = Color.FromArgb(10, 10, 12);
        _content.Margin = new Padding(0);
        _content.Padding = new Padding(0);

        BuildTopBar();

        Controls.Add(_content);
        Controls.Add(_topBar);

        KeyDown += OnKeyDown;
        Load += async (_, _) => await BuildAgentsAsync();
        Shown += async (_, _) => await OnShownAsync();
        FormClosing += OnClosing;

        _timer.Interval = 2000;
        _timer.Tick += (_, _) =>
        {
            RefreshAllStates();
            UpdateStatus();
        };
        _timer.Start();

        _anim.Interval = 60;
        _anim.Tick += (_, _) => TickBreathing();

        _focusTimer.Interval = 250;
        _focusTimer.Tick += (_, _) => KeepPointerFocus();
        _focusTimer.Start();
    }

    private void BuildTopBar()
    {
        _topBar = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 36,
            BackColor = Color.FromArgb(10, 10, 12),
            Margin = new Padding(0),
            Padding = new Padding(0),
            ColumnCount = 3,
            RowCount = 1
        };

        _topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _topBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _topBar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _chipFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.FromArgb(10, 10, 12),
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        _statusLabel.AutoSize = false;
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.ForeColor = Color.FromArgb(130, 130, 140);
        _statusLabel.Font = new Font("Microsoft YaHei UI", 9f);
        _statusLabel.Padding = new Padding(12, 0, 0, 0);
        _statusLabel.BackColor = Color.FromArgb(10, 10, 12);

        _actionFlow = new FlowLayoutPanel
        {
            AutoSize = false,
            Width = 6 * 64 + 30,
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.FromArgb(10, 10, 12),
            Margin = new Padding(0),
            Padding = new Padding(0)
        };

        _btnOpen = MakeAction("打开", OnOpen);
        _btnOpen.Width = 84;
        _btnClose = MakeAction("关闭", OnCloseAgent);
        _btnDetach = MakeAction("脱离", (_, _) => CurrentHost()?.Detach());
        _btnRestart = MakeAction("重启", OnRestart);
        _btnReattach = MakeAction("重连", async (_, _) => await ReattachAsync());
        _btnSplit = MakeAction("分屏", (_, _) => PromptSplitAsync());

        _actionFlow.Controls.AddRange(new Control[]
        {
            _btnOpen, _btnClose, _btnDetach, _btnRestart, _btnReattach, _btnSplit
        });

        _topBar.Controls.Add(_chipFlow, 0, 0);
        _topBar.Controls.Add(_statusLabel, 1, 0);
        _topBar.Controls.Add(_actionFlow, 2, 0);

        _topBar.Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(24, 24, 29));
            e.Graphics.DrawLine(pen, 0, _topBar.Height - 1, _topBar.Width, _topBar.Height - 1);
        };
    }

    private static Button MakeAction(string text, EventHandler onClick)
    {
        var b = new Button
        {
            Text = text,
            Width = 62,
            Height = 26,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(200, 200, 210),
            BackColor = Color.FromArgb(10, 10, 12),
            Font = new Font("Microsoft YaHei UI", 9f),
            Margin = new Padding(1, 5, 1, 5),
            TabStop = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Color.FromArgb(28, 28, 33);
        b.FlatAppearance.MouseDownBackColor = Color.FromArgb(38, 38, 44);
        b.HandleCreated += (_, _) =>
        {
            using var path = Rounded(new Rectangle(0, 0, b.Width, b.Height), 6);
            b.Region = new Region(path);
        };
        b.SizeChanged += (_, _) =>
        {
            using var path = Rounded(new Rectangle(0, 0, b.Width, b.Height), 6);
            b.Region = new Region(path);
        };
        b.Click += onClick;
        return b;
    }

    private static System.Drawing.Drawing2D.GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void KeepPointerFocus()
    {
        if (WindowState == FormWindowState.Minimized) return;

        foreach (var h in _hosts.Values)
            if (h.Embedded) h.ResizeChild();

        if (Native.GetForegroundWindow() != Handle) return;
        if (!Native.GetCursorPos(out var pt)) return;

        var hwnd = Native.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero) return;
        if (hwnd == _topBar.Handle || Native.IsChild(_topBar.Handle, hwnd)) return;

        foreach (var host in _hosts.Values)
        {
            if (!host.OwnsWindow(hwnd)) continue;
            if (!host.HasKeyboardFocus()) host.FocusChild();
            return;
        }
    }

    private async Task BuildAgentsAsync()
    {
        List<AgentDef> defs;
        try
        {
            var json = File.ReadAllText(_configPath);
            defs = JsonSerializer.Deserialize<List<AgentDef>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<AgentDef>();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "读取 agents.json 失败：" + Environment.NewLine + ex.Message,
                "AgentHub", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        foreach (var def in defs)
        {
            var panel = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(10, 10, 12),
                Margin = new Padding(0),
                Visible = false
            };
            _content.Controls.Add(panel);

            var hint = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(110, 110, 120),
                Font = new Font("Microsoft YaHei UI", 10f),
                Text = ""
            };
            _content.Controls.Add(hint);
            panel.BringToFront();
            _tabHints[def.Key] = hint;

            var host = new AgentHost(def, panel);
            host.Changed += () => BeginInvoke(() =>
            {
                RefreshAllStates();
                UpdateStatus();
            });
            _hosts[def.Key] = host;
            _keys.Add(def.Key);

            var chip = new TabChip
            {
                Key = def.Key,
                Text = def.Title,
                Accent = ParseColor(def.Accent)
            };
            chip.Activated += (_, _) =>
            {
                var idx = _chips.IndexOf(chip);
                if (idx >= 0) SelectIndex(idx);
            };
            _chipFlow.Controls.Add(chip);
            _chips.Add(chip);
            _chipByKey[def.Key] = chip;
        }

        BuildSplitSurface();
        UpdateStatus();

        _startupKey = Environment.GetCommandLineArgs()
            .Select(a => a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase) ? a[6..] : null)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        await Task.CompletedTask;
    }

    private void BuildSplitSurface()
    {
        _splitGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(10, 10, 12),
            Margin = new Padding(0),
            Padding = new Padding(0),
            Visible = false
        };

        for (var i = 0; i < _cells.Length; i++)
        {
            _cells[i] = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(8, 8, 10),
                Margin = new Padding(1)
            };

            _cellHint[i] = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Color.FromArgb(110, 110, 120),
                Font = new Font("Microsoft YaHei UI", 10f),
                Text = ""
            };
            _cells[i].Controls.Add(_cellHint[i]);
        }

        _content.Controls.Add(_splitGrid);
        ConfigureGrid(2);

        var splitChip = new TabChip
        {
            Key = "__split__",
            Text = "分屏",
            Accent = Color.FromArgb(120, 120, 130)
        };
        splitChip.Activated += (_, _) => SelectIndex(_keys.Count);
        _chipFlow.Controls.Add(splitChip);
        _chips.Add(splitChip);
    }

    private void ConfigureGrid(int count)
    {
        _splitGrid.SuspendLayout();

        foreach (var c in _cells)
            if (c.Parent != null) _splitGrid.Controls.Remove(c);

        _splitGrid.ColumnStyles.Clear();
        _splitGrid.RowStyles.Clear();

        _splitGrid.ColumnCount = 2;
        _splitGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _splitGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

        if (count <= 2)
        {
            _splitGrid.RowCount = 1;
            _splitGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }
        else
        {
            _splitGrid.RowCount = 2;
            _splitGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            _splitGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        }

        for (var i = 0; i < _cells.Length && i < count; i++)
        {
            var row = count <= 2 ? 0 : i / 2;
            var col = count <= 2 ? i : i % 2;
            _splitGrid.Controls.Add(_cells[i], col, row);
        }

        _splitGrid.ResumeLayout(true);
    }

    private void TickBreathing()
    {
        _phase += 0.16;
        var any = false;

        foreach (var chip in _chips)
        {
            if (!_hosts.TryGetValue(chip.Key, out var host)) continue;
            if (host.State != AgentState.Running) continue;
            chip.SetPhase(_phase);
            any = true;
        }

        if (!any) _anim.Stop();
    }

    private void RefreshAllStates()
    {
        var breathing = false;

        foreach (var kv in _hosts)
        {
            var host = kv.Value;
            host.RefreshState();

            if (_chipByKey.TryGetValue(kv.Key, out var chip))
                chip.SetState(host.State);

            if (host.State == AgentState.Running) breathing = true;
        }

        UpdateTabHints();
        UpdateCellHints();
        if (breathing && !_anim.Enabled) _anim.Start();
    }

    private void UpdateTabHints()
    {
        foreach (var kv in _hosts)
        {
            if (!_tabHints.TryGetValue(kv.Key, out var hint)) continue;

            var idx = _keys.IndexOf(kv.Key);
            var show = idx == _selected && !kv.Value.Embedded;
            var panel = kv.Value.Container;
            var inSplit = !ReferenceEquals(panel.Parent, _content);

            panel.Visible = inSplit || (idx == _selected && kv.Value.Embedded);

            hint.Visible = show;

            if (!show)
            {
                hint.Text = "";
                continue;
            }

            hint.Text = kv.Value.ProcessRunning
                ? kv.Value.Def.Title + " 正在后台运行" + Environment.NewLine + "点「打开」收进这个标签页"
                : kv.Value.Def.Title + " 未启动" + Environment.NewLine + "点「打开」启动";
        }
    }

    private void UpdateCellHints()
    {
        if (_splitKeys.Count == 0) return;

        for (var i = 0; i < _splitKeys.Count && i < _cellHint.Length; i++)
        {
            var host = _hosts[_splitKeys[i]];
            _cellHint[i].Text = host.Embedded
                ? ""
                : host.Def.Title + " 未启动";
        }
    }

    private void SelectIndex(int idx)
    {
        if (!_ready) return;
        if (idx < 0 || idx > _keys.Count) return;

        _selected = idx;
        for (var i = 0; i < _chips.Count; i++) _chips[i].SetSelected(i == idx);

        if (idx == _keys.Count)
        {
            _ = EnterSplitAsync();
            return;
        }

        LeaveSplitLayout();
        _splitGrid.Visible = false;

        var key = _keys[idx];
        foreach (var kv in _hosts)
        {
            var on = kv.Key == key;
            kv.Value.MoveTo(_content);
            kv.Value.Show(on);
        }

        var host = _hosts[key];
        host.Container.BringToFront();
        RefreshAllStates();
        host.FocusChild();
        _ = host.EnsureStartedAsync();
        UpdateStatus();
    }

    private async Task EnterSplitAsync()
    {
        if (_splitKeys.Count < 2 && !PromptSplit())
        {
            SelectIndex(0);
            return;
        }

        _splitGrid.Visible = true;
        _splitGrid.BringToFront();
        await LayoutSplitAsync();
    }

    private async Task LayoutSplitAsync()
    {
        if (_layoutBusy) return;
        _layoutBusy = true;

        try
        {
            ConfigureGrid(_splitKeys.Count);
            _splitGrid.Visible = true;
            _splitGrid.BringToFront();

            foreach (var kv in _hosts)
                kv.Value.Container.Visible = false;

            for (var i = 0; i < _splitKeys.Count; i++)
            {
                var host = _hosts[_splitKeys[i]];

                if (!host.ProcessRunning && !host.Embedded)
                {
                    _cellHint[i].Text = host.Def.Title + " 未启动";
                    host.Container.Visible = false;
                    host.Show(false);
                    continue;
                }

                host.MoveTo(_cells[i]);
                host.Container.Visible = true;
                host.Show(true);
                await host.EnsureStartedAsync();
                await Task.Delay(120);
            }

            UpdateCellHints();
            UpdateStatus();
        }
        finally
        {
            _layoutBusy = false;
        }
    }

    private void LeaveSplitLayout()
    {
        if (_splitKeys.Count == 0) return;
        foreach (var kv in _hosts)
            kv.Value.MoveTo(_content);
    }

    private bool PromptSplit()
    {
        using var dlg = new SplitDialog(_hosts.Values
            .Select(h => (h.Def.Key, h.Def.Title))
            .ToList(), _splitKeys);

        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        if (dlg.SelectedKeys.Count < 2)
        {
            MessageBox.Show(this, "分屏至少选 2 个。", "AgentHub",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return false;
        }

        _splitKeys.Clear();
        _splitKeys.AddRange(dlg.SelectedKeys);
        foreach (var k in _splitKeys) _hosts[k].AutoAttach = true;
        return true;
    }

    private async void PromptSplitAsync()
    {
        if (!PromptSplit()) return;
        _selected = _keys.Count;
        for (var i = 0; i < _chips.Count; i++) _chips[i].SetSelected(i == _selected);
        _splitGrid.Visible = true;
        _splitGrid.BringToFront();
        await LayoutSplitAsync();
    }

    private AgentHost? CurrentHost()
    {
        if (_selected < 0 || _selected >= _keys.Count) return null;
        return _hosts.TryGetValue(_keys[_selected], out var host) ? host : null;
    }

    private async void OnOpen(object? sender, EventArgs e)
    {
        var host = CurrentHost();
        if (host == null) return;
        await host.EnsureStartedAsync();
    }

    private void OnCloseAgent(object? sender, EventArgs e)
    {
        var host = CurrentHost();
        if (host == null) return;

        if (MessageBox.Show(this, "关闭 " + host.Def.Title + "？" + Environment.NewLine +
                "未保存的对话会丢失。", "确认关闭",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        host.KillProcess();
        RefreshAllStates();
        UpdateStatus();
    }

    private async Task ReattachAsync()
    {
        var host = CurrentHost();
        if (host == null) return;
        host.Detach();
        await Task.Delay(300);
        await host.EnsureStartedAsync();
    }

    private async void OnRestart(object? sender, EventArgs e)
    {
        var host = CurrentHost();
        if (host == null) return;
        if (MessageBox.Show(this, "结束并重启 " + host.Def.Title + "？未保存的对话可能丢失。",
                "确认", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK)
            return;

        host.KillProcess();
        await Task.Delay(1500);
        await host.EnsureStartedAsync();
    }

    private void UpdateStatus()
    {
        if (_selected == _keys.Count)
        {
            SetSingleButtonsEnabled(false);
            _btnOpen.Text = "打开";
            var names = _splitKeys.Select(k => _hosts[k].Def.Title).ToList();
            _statusLabel.Text = "分屏：" + (names.Count > 0 ? string.Join(" + ", names) : "未选择");
            return;
        }

        var host = CurrentHost();
        SetSingleButtonsEnabled(host != null);
        _btnOpen.Text = host != null && host.PendingConfirm ? "确认接管" : "打开";
        _statusLabel.Text = host == null ? "" : host.Def.Title + "  ·  " + host.Status;
    }

    private void SetSingleButtonsEnabled(bool enabled)
    {
        _btnOpen.Enabled = enabled;
        _btnClose.Enabled = enabled;
        _btnDetach.Enabled = enabled;
        _btnRestart.Enabled = enabled;
        _btnReattach.Enabled = enabled;
    }

    private static Color ParseColor(string hex)
    {
        try { return ColorTranslator.FromHtml(hex); }
        catch { return Color.Gray; }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D9)
        {
            var idx = e.KeyCode - Keys.D1;
            if (idx <= _keys.Count) SelectIndex(idx);
            e.Handled = true;
        }
        else if (e.Control && e.KeyCode == Keys.Tab)
        {
            SelectIndex((_selected + 1) % (_keys.Count + 1));
            e.Handled = true;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        _hotKeyCount = 4;
        for (var i = 0; i < _hotKeyCount; i++)
        {
            Native.RegisterHotKey(Handle, HotKeyBaseId + i,
                Native.MOD_CONTROL | Native.MOD_ALT, (uint)Keys.D1 + (uint)i);
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        for (var i = 0; i < _hotKeyCount; i++)
            Native.UnregisterHotKey(Handle, HotKeyBaseId + i);
        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY)
        {
            var id = m.WParam.ToInt32() - HotKeyBaseId;
            if (id >= 0 && id <= _keys.Count)
            {
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
                SelectIndex(id);
            }
            return;
        }
        base.WndProc(ref m);
    }

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        _timer.Stop();
        _anim.Stop();
        _focusTimer.Stop();
        foreach (var h in _hosts.Values)
        {
            if (h.Embedded) h.Detach();
        }
    }

    private async Task OnShownAsync()
    {
        _ready = true;

        var splitArg = Environment.GetCommandLineArgs()
            .Select(a => a.StartsWith("--split=", StringComparison.OrdinalIgnoreCase) ? a[8..] : null)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

        if (splitArg != null)
        {
            var keys = splitArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(k => _hosts.ContainsKey(k)).Take(4).ToList();
            if (keys.Count >= 2)
            {
                foreach (var k in keys) _hosts[k].AutoAttach = true;
                _splitKeys.Clear();
                _splitKeys.AddRange(keys);
                _selected = _keys.Count;
                for (var i = 0; i < _chips.Count; i++) _chips[i].SetSelected(i == _selected);
                _splitGrid.Visible = true;
                _splitGrid.BringToFront();
                await LayoutSplitAsync();
                return;
            }
        }

        if (_startupKey != null && _hosts.TryGetValue(_startupKey, out var startHost))
        {
            startHost.AutoAttach = true;
            var idx = _keys.IndexOf(_startupKey);
            if (idx >= 0) SelectIndex(idx);
        }
        else
        {
            SelectIndex(0);
        }
    }
}
