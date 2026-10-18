using System.Diagnostics;
using System.Windows.Forms;

namespace AgentHub;

internal sealed class AgentDef
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public string Launch { get; set; } = "";
    public string ProcessName { get; set; } = "";
    public string Accent { get; set; } = "#808080";
}

internal enum AgentState
{
    Stopped,
    Starting,
    Running,
    Attached,
    Failed
}

internal sealed class AgentHost
{
    private static readonly HashSet<IntPtr> Claimed = new();

    public AgentDef Def { get; }
    public Panel Container { get; }

    public IntPtr Window { get; private set; } = IntPtr.Zero;
    public bool Embedded => Window != IntPtr.Zero && Native.IsWindow(Window);
    public bool Starting { get; private set; }
    public string Status { get; private set; } = "未启动";
    public AgentState State { get; private set; } = AgentState.Stopped;
    public bool AutoAttach { get; set; }
    public bool PendingConfirm { get; private set; }

    private long _origStyle;
    private long _origExStyle;
    private bool _styleCaptured;
    private Native.RECT _origRect;
    private int _lastW = -1;
    private int _lastH = -1;

    public event Action? Changed;

    public AgentHost(AgentDef def, Panel container)
    {
        Def = def;
        Container = container;
        Container.Resize += (_, _) => ResizeChild();
    }

    public async Task EnsureStartedAsync()
    {
        if (Starting) return;

        if (Embedded)
        {
            Show(true);
            return;
        }

        Starting = true;
        State = AgentState.Starting;
        Status = "启动中…";
        Changed?.Invoke();

        try
        {
            var hwnd = FindExistingWindow();
            if (hwnd != IntPtr.Zero && !AutoAttach && !PendingConfirm)
            {
                PendingConfirm = true;
                State = AgentState.Stopped;
                Status = "已在运行 —— 再点一次「打开」就收进标签页";
                return;
            }
            PendingConfirm = false;
            if (hwnd == IntPtr.Zero)
            {
                LaunchProcess();
                hwnd = await WaitForWindowAsync(TimeSpan.FromSeconds(90));
            }

            if (hwnd == IntPtr.Zero)
            {
                State = AgentState.Failed;
                Status = "没等到窗口（可能被拦或还在加载），点「重新接管」";
                return;
            }

            await EmbedAsync(hwnd);
            State = AgentState.Attached;
            Status = "已接管";

            // Electron 重新挂载后常常首帧是黑的，等它自己重绘一次
            await Task.Delay(800);
            ForceRepaint();
            await Task.Delay(1500);
            ForceRepaint();

            // 刚挂载的窗口不会自动拿到键盘焦点，主动给一次
            FocusChild();
        }
        catch (Exception ex)
        {
            State = AgentState.Failed;
            Status = "失败: " + ex.Message;
        }
        finally
        {
            Starting = false;
            Changed?.Invoke();
        }
    }

    private void LaunchProcess()
    {
        var target = Environment.ExpandEnvironmentVariables(Def.Launch);
        if (!File.Exists(target))
            throw new FileNotFoundException("找不到启动目标: " + target);

        var psi = new ProcessStartInfo(target) { UseShellExecute = true };
        Process.Start(psi);
    }

    private IntPtr FindExistingWindow()
    {
        return PickWindow(CollectWindows(TargetPids()));
    }

    private HashSet<uint> TargetPids()
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(Def.ProcessName))
        {
            try { pids.Add((uint)p.Id); } catch { }
        }
        return pids;
    }

    private static List<IntPtr> CollectWindows(HashSet<uint> pids)
    {
        var found = new List<IntPtr>();
        Native.EnumWindows((h, _) =>
        {
            Native.GetWindowThreadProcessId(h, out var pid);
            if (!pids.Contains(pid)) return true;
            if (!Native.IsWindowVisible(h)) return true;
            if (Native.GetWindowTextLength(h) == 0) return true;
            if (Claimed.Contains(h)) return true;
            var ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;
            found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static IntPtr PickWindow(List<IntPtr> windows)
    {
        IntPtr best = IntPtr.Zero;
        long bestArea = -1;
        foreach (var h in windows)
        {
            if (!Native.GetWindowRect(h, out var r)) continue;
            long area = (long)Math.Abs(r.Right - r.Left) * Math.Abs(r.Bottom - r.Top);
            if (area > bestArea)
            {
                bestArea = area;
                best = h;
            }
        }
        return best;
    }

    private async Task<IntPtr> WaitForWindowAsync(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        long lastTick = 0;

        while (sw.Elapsed < timeout)
        {
            var hwnd = PickWindow(CollectWindows(TargetPids()));
            if (hwnd != IntPtr.Zero) return hwnd;

            if (sw.ElapsedMilliseconds - lastTick > 3000)
            {
                lastTick = sw.ElapsedMilliseconds;
                Status = $"启动中… 已等 {(int)sw.Elapsed.TotalSeconds}s（等窗口出现）";
                Changed?.Invoke();
            }

            await Task.Delay(250);
        }
        return IntPtr.Zero;
    }

    private async Task EmbedAsync(IntPtr hwnd)
    {
        var parent = Native.GetParent(hwnd);
        if (parent != IntPtr.Zero) Native.SetParent(hwnd, IntPtr.Zero);

        // 最小化的窗口必须在成为子窗口之前先还原，否则 Electron 会渲染成黑块
        if (Native.IsIconic(hwnd) || Native.IsZoomed(hwnd))
        {
            Native.ShowWindow(hwnd, Native.SW_RESTORE);
            await Task.Delay(200);
        }

        if (!_styleCaptured)
        {
            _origStyle = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
            _origExStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.GetWindowRect(hwnd, out _origRect);
            _styleCaptured = true;
        }

        long style = Native.GetWindowLong(hwnd, Native.GWL_STYLE);
        style &= ~(Native.WS_CAPTION | Native.WS_THICKFRAME | Native.WS_MINIMIZEBOX |
                   Native.WS_MAXIMIZEBOX | Native.WS_SYSMENU | Native.WS_POPUP);
        style |= Native.WS_CHILD;
        Native.SetWindowLong(hwnd, Native.GWL_STYLE, style);

        long ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
        ex &= ~Native.WS_EX_APPWINDOW;
        ex |= Native.WS_EX_TOOLWINDOW;
        Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex);

        Native.SetParent(hwnd, Container.Handle);
        _lastW = -1;
        _lastH = -1;
        Window = hwnd;
        Claimed.Add(hwnd);

        Native.SetWindowPos(hwnd, Native.HWND_TOP, 0, 0,
            Math.Max(1, Container.ClientSize.Width), Math.Max(1, Container.ClientSize.Height),
            Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);

        Native.ShowWindow(hwnd, Native.SW_RESTORE);
        ResizeChild();
        ForceRepaint();
    }

    public void MoveTo(Control parent)
    {
        if (parent == null) return;
        if (ReferenceEquals(parent, Container)) return;
        if (ReferenceEquals(Container.Parent, parent)) return;

        var targetChain = Chain(parent);
        var ownChain = Chain(Container);

        try
        {
            if (Container.Parent != null) Container.Parent = null;
            parent.Controls.Add(Container);
        }
        catch (Exception ex)
        {
            Log("MoveTo target=[ " + targetChain + " ] container=[ " + ownChain + " ] " + ex.GetType().Name);
            return;
        }

        Container.Dock = DockStyle.Fill;
        Container.Visible = true;
        Container.BringToFront();
        _lastW = -1;
        _lastH = -1;

        if (Embedded)
        {
            Container.CreateControl();
            AttachToContainer();

            // 重设父级会重建容器句柄，延后一帧再挂一次，避免子窗口挂在已销毁的句柄上
            try
            {
                Container.BeginInvoke(new Action(() =>
                {
                    if (Embedded)
                    {
                        AttachToContainer();
                        Native.ShowWindow(Window, Native.SW_SHOW);
                        ForceRepaint();
                    }
                }));
            }
            catch { }
        }
    }

    private void AttachToContainer()
    {
        Native.SetParent(Window, Container.Handle);
        Native.SetWindowPos(Window, Native.HWND_TOP, 0, 0,
            Math.Max(1, Container.ClientSize.Width), Math.Max(1, Container.ClientSize.Height),
            Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);
    }

    private static string Chain(Control? c)
    {
        var parts = new List<string>();
        while (c != null && parts.Count < 10)
        {
            parts.Add(c.GetType().Name + "#" + c.Name);
            c = c.Parent;
        }
        return string.Join(" > ", parts);
    }

    private static void Log(string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "crash.log"),
                "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
        }
        catch { }
    }

    public void ForceRepaint()
    {
        if (!Embedded) return;
        var w = Math.Max(1, Container.ClientSize.Width);
        var h = Math.Max(1, Container.ClientSize.Height);
        Native.MoveWindow(Window, 0, 0, w, h, true);
        Native.RedrawWindow(Window, IntPtr.Zero, IntPtr.Zero,
            Native.RDW_INVALIDATE | Native.RDW_ALLCHILDREN | Native.RDW_UPDATENOW | Native.RDW_FRAME);
    }

    public void Show(bool visible)
    {
        if (!Embedded) return;
        Native.ShowWindow(Window, visible ? Native.SW_SHOW : Native.SW_HIDE);
        if (visible)
        {
            ResizeChild();
            Native.BringWindowToTop(Window);
        }
    }

    public void ResizeChild()
    {
        if (!Embedded) return;

        var w = Math.Max(1, Container.ClientSize.Width);
        var h = Math.Max(1, Container.ClientSize.Height);
        if (w == _lastW && h == _lastH) return;

        _lastW = w;
        _lastH = h;
        Native.MoveWindow(Window, 0, 0, w, h, true);
    }

    public void FocusChild()
    {
        if (!Embedded) return;

        // SetFocus 只对同线程窗口有效，跨进程要临时挂线程输入队列
        var tid = Native.GetWindowThreadProcessId(Window, out _);
        var myTid = Native.GetCurrentThreadId();
        if (tid != 0 && tid != myTid)
        {
            Native.AttachThreadInput(myTid, tid, true);
            Native.SetFocus(Window);
            Native.AttachThreadInput(myTid, tid, false);
        }
        else
        {
            Native.SetFocus(Window);
        }
    }

    public bool OwnsWindow(IntPtr hwnd)
    {
        if (!Embedded || hwnd == IntPtr.Zero) return false;
        return hwnd == Window || Native.IsChild(Window, hwnd);
    }

    public bool HasKeyboardFocus()
    {
        if (!Embedded) return false;
        var tid = Native.GetWindowThreadProcessId(Window, out _);
        if (tid == 0) return false;

        var info = new Native.GUITHREADINFO
        {
            cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.GUITHREADINFO>()
        };
        if (!Native.GetGUIThreadInfo(tid, ref info)) return false;

        var focus = info.hwndFocus;
        if (focus == IntPtr.Zero) return false;
        return focus == Window || Native.IsChild(Window, focus);
    }

    public void Detach()
    {
        if (!Embedded) return;
        var hwnd = Window;
        Claimed.Remove(hwnd);

        Native.ShowWindow(hwnd, Native.SW_HIDE);
        Native.SetParent(hwnd, IntPtr.Zero);

        int w = Math.Max(600, _origRect.Right - _origRect.Left);
        int hh = Math.Max(400, _origRect.Bottom - _origRect.Top);
        Native.SetWindowPos(hwnd, IntPtr.Zero, _origRect.Left, _origRect.Top, w, hh,
            Native.SWP_NOZORDER | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);

        if (_styleCaptured)
        {
            Native.SetWindowLong(hwnd, Native.GWL_STYLE, _origStyle);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, _origExStyle);
        }

        Window = IntPtr.Zero;
        State = ProcessRunning ? AgentState.Running : AgentState.Stopped;
        Status = "已脱离";
        Changed?.Invoke();
    }

    public void KillProcess()
    {
        foreach (var p in Process.GetProcessesByName(Def.ProcessName))
        {
            try { p.Kill(true); } catch { }
        }

        if (Window != IntPtr.Zero)
        {
            Claimed.Remove(Window);
            Window = IntPtr.Zero;
        }

        State = AgentState.Stopped;
        PendingConfirm = false;
        Status = "已结束";
        Changed?.Invoke();
    }

    public bool ProcessRunning => Process.GetProcessesByName(Def.ProcessName).Length > 0;

    public void RefreshState()
    {
        var running = ProcessRunning;

        if (State == AgentState.Attached && !Embedded)
        {
            State = running ? AgentState.Running : AgentState.Stopped;
            Status = running ? "进程在跑，未接管" : "未启动";
        }
        else if (State == AgentState.Attached && !running)
        {
            State = AgentState.Stopped;
            Status = "进程已退出";
        }
        else if (State == AgentState.Stopped && running)
        {
            State = AgentState.Running;
            Status = "进程在跑，未接管";
        }
        else if (State == AgentState.Running && !running)
        {
            State = AgentState.Stopped;
            Status = "未启动";
        }
    }
}
