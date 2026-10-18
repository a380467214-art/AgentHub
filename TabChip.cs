using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AgentHub;

internal sealed class TabChip : Control
{
    private AgentState _state = AgentState.Stopped;
    private double _phase;
    private bool _selected;
    private bool _hover;

    public string Key = "";
    public Color Accent = Color.Gray;
    public event EventHandler? Activated;

    public TabChip()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        Size = new Size(132, 30);
        Cursor = Cursors.Hand;
        Font = new Font("Microsoft YaHei UI", 9.5f);
    }

    public void SetSelected(bool value)
    {
        if (_selected == value) return;
        _selected = value;
        Invalidate();
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

    protected override void OnMouseEnter(EventArgs e)
    {
        _hover = true;
        Invalidate();
        base.OnMouseEnter(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = false;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Activated?.Invoke(this, EventArgs.Empty);
        base.OnMouseDown(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(Color.FromArgb(10, 10, 12)))
            g.FillRectangle(bg, ClientRectangle);

        var pill = new Rectangle(3, 3, Width - 6, Height - 6);
        var pillColor = _selected
            ? Color.FromArgb(26, 26, 31)
            : _hover ? Color.FromArgb(18, 18, 22) : Color.FromArgb(13, 13, 16);

        using (var path = Rounded(pill, 7))
        using (var brush = new SolidBrush(pillColor))
            g.FillPath(brush, path);

        using (var bar = new SolidBrush(_selected ? Accent : Color.FromArgb(42, 42, 48)))
            g.FillRectangle(bar, 10, 9, 3, Height - 18);

        var textRect = new Rectangle(19, 0, Width - 51, Height);
        TextRenderer.DrawText(g, Text, Font, textRect,
            _selected ? Color.FromArgb(248, 248, 250) : Color.FromArgb(138, 138, 148),
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);

        var dotColor = _state switch
        {
            AgentState.Attached => Color.FromArgb(52, 199, 89),
            AgentState.Running => Color.FromArgb(
                (int)(65 + 190 * ((Math.Sin(_phase) + 1) / 2.0)), 52, 199, 89),
            AgentState.Starting => Color.FromArgb(245, 165, 36),
            AgentState.Failed => Color.FromArgb(255, 69, 58),
            _ => Color.FromArgb(78, 78, 86)
        };

        if (_state == AgentState.Attached || _state == AgentState.Running)
        {
            using var glow = new SolidBrush(Color.FromArgb(
                _state == AgentState.Attached ? 46 : 28, dotColor));
            g.FillEllipse(glow, Width - 25, Height / 2 - 9, 18, 18);
        }

        using (var dot = new SolidBrush(dotColor))
            g.FillEllipse(dot, Width - 20, Height / 2 - 4, 9, 9);

        if (_selected)
        {
            using var line = new SolidBrush(Accent);
            g.FillRectangle(line, pill.X + 6, Height - 5, pill.Width - 12, 2);
        }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();
        var d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
