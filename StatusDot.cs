using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgentHub;

internal sealed class StatusDot : Control
{
    private AgentState _state = AgentState.Stopped;
    private double _phase;
    private bool _onSelectedTab;

    public StatusDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        Size = new Size(10, 10);
        BackColor = Color.FromArgb(10, 10, 12);
        Cursor = Cursors.Hand;
    }

    public void SetState(AgentState value)
    {
        if (_state == value) return;
        _state = value;
        Invalidate();
    }

    public void SetPhase(double value)
    {
        _phase = value;
        if (_state == AgentState.Running) Invalidate();
    }

    public void SyncTabLook(bool selected)
    {
        _onSelectedTab = selected;
        var bg = selected ? Color.FromArgb(18, 18, 21) : Color.FromArgb(10, 10, 12);
        if (BackColor != bg) BackColor = bg;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var color = _state switch
        {
            AgentState.Attached => Color.FromArgb(52, 199, 89),
            AgentState.Running => Color.FromArgb(
                (int)(70 + 185 * ((Math.Sin(_phase) + 1) / 2.0)), 52, 199, 89),
            AgentState.Starting => Color.FromArgb(245, 165, 36),
            AgentState.Failed => Color.FromArgb(255, 69, 58),
            _ => Color.FromArgb(95, 95, 102)
        };

        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var brush = new SolidBrush(color);
        g.FillEllipse(brush, 1, 1, Width - 2, Height - 2);
    }
}
