using System.Runtime.InteropServices;

namespace BTSMTL.WebWorkbench.Shell;

// 画面区：后端渲染窗口以它为父窗口；画面区改大小时把直接子窗口铺满，网页浮层所在处挖空。
// 子窗口属于另一进程，同步等它（SendMessage、MoveWindow）会与它同步回调父窗口互相等死；对子窗口只投递、只异步改位置。
// 嵌入约定：子窗口在 GWLP_USERDATA 第 0 位声明图形已就绪、第 1 位声明上次改尺寸已完成，两位都置上才改它的尺寸。
sealed class NativeSurface : Control
{
    const int WmActivate = 0x0006, GwlpUserData = -21;
    const long ChildReady = 0b11;
    const uint SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010, SwpAsyncWindowPos = 0x4000;

    readonly System.Windows.Forms.Timer m_PendingFit = new() { Interval = 100 };

    public NativeSurface()
    {
        BackColor = Color.Black;
        SetStyle(ControlStyles.Selectable, false);
        m_PendingFit.Tick += (_, _) => FitChildren();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            m_PendingFit.Dispose();
        base.Dispose(disposing);
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
            PostMessage(child, WmActivate, active ? 1 : 0, IntPtr.Zero);
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
        bool pending = false;
        foreach (IntPtr child in Children())
        {
            if (!GetWindowRect(child, out NativeRect rect) ||
                (rect.Right - rect.Left == ClientSize.Width && rect.Bottom - rect.Top == ClientSize.Height))
                continue;
            if ((GetWindowLongPtr(child, GwlpUserData).ToInt64() & ChildReady) != ChildReady)
            {
                pending = true;
                continue;
            }
            SetWindowPos(child, IntPtr.Zero, 0, 0, ClientSize.Width, ClientSize.Height,
                SwpNoZOrder | SwpNoActivate | SwpAsyncWindowPos);
        }
        m_PendingFit.Enabled = pending;
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
    static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    static extern bool PostMessage(IntPtr window, int message, int wParam, IntPtr lParam);
}
