using System.Windows.Forms;

namespace AgentHub;

internal sealed class BufferedTabControl : TabControl
{
    public BufferedTabControl()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        UpdateStyles();
    }

    protected override void WndProc(ref Message m)
    {
        // WM_ERASEBKGND 会造成整条标签栏闪白
        if (m.Msg == 0x0014) return;
        base.WndProc(ref m);
    }
}
