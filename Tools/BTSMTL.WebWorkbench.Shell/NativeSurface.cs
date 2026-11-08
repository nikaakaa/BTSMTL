using System.Runtime.InteropServices;

namespace BTSMTL.WebWorkbench.Shell;

// 画面区：后端渲染窗口以它为父窗口；画面区改大小时把直接子窗口铺满，网页浮层所在处挖空。
sealed class NativeSurface : Control
{
    const int WmActivate = 0x0006;

    public NativeSurface()
    {
        BackColor = Color.Black;
        SetStyle(ControlStyles.Selectable, false);
    }

    public void Place(Rectangle bounds, Rectangle[] holes)
    {
        if (Bounds != bounds)
            Bounds = bounds;
        if (holes.Length == 0)
            Region = null;
        else
        {
            var region = new Region(new Rectangle(Point.Empty, bounds.Size));
            foreach (Rectangle hole in holes)
            {
                Rectangle local = hole;
                local.Offset(-bounds.X, -bounds.Y);
                region.Exclude(local);
            }
            Region = region;
        }
        FitChildren();
    }

    public void Activate(bool active)
    {
        foreach (IntPtr child in Children())
            SendMessage(child, WmActivate, active ? 1 : 0, IntPtr.Zero);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        FitChildren();
    }

    void FitChildren()
    {
        if (!IsHandleCreated)
            return;
        foreach (IntPtr child in Children())
            if (GetWindowRect(child, out NativeRect rect) &&
                (rect.Right - rect.Left != ClientSize.Width || rect.Bottom - rect.Top != ClientSize.Height))
                MoveWindow(child, 0, 0, ClientSize.Width, ClientSize.Height, true);
    }

    List<IntPtr> Children()
    {
        var result = new List<IntPtr>();
        if (!IsHandleCreated)
            return result;
        IntPtr parent = Handle;
        EnumChildWindows(parent, (child, _) =>
        {
            if (GetParent(child) == parent)
                result.Add(child);
            return true;
        }, IntPtr.Zero);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct NativeRect { public int Left, Top, Right, Bottom; }

    delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    static extern IntPtr GetParent(IntPtr window);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    static extern bool MoveWindow(IntPtr window, int x, int y, int width, int height, bool repaint);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr window, int message, int wParam, IntPtr lParam);
}
