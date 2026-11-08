using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BTSMTL.WebWorkbench.Shell;

// 工作台桌面壳：网页界面由 WebView2 显示。网页按画面区申请共享内存，壳建好后只读交给网页，
// 并按网页给出的进程号把映射句柄复制进后端渲染进程；每帧像素由后端直接写、网页直接读，壳不经手也不认识引擎和布局。
sealed class ShellForm : Form
{
    const uint DuplicateSameAccess = 2;

    readonly Uri m_Address;
    readonly WebView2 m_View = new() { Dock = DockStyle.Fill };
    readonly Dictionary<string, CoreWebView2SharedBuffer> m_Surfaces = new(StringComparer.Ordinal);
    readonly SurfaceTiming? m_Timing;
    CoreWebView2Environment? m_Environment;

    public ShellForm(Uri address, SurfaceTiming? timing)
    {
        m_Address = address;
        m_Timing = timing;
        Text = "BTSMTL 工作台";
        ClientSize = new Size(1600, 960);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(m_View);
        Load += async (_, _) => await Open();
        FormClosed += (_, _) =>
        {
            foreach (CoreWebView2SharedBuffer buffer in m_Surfaces.Values)
                buffer.Dispose();
            m_Timing?.Write();
        };
    }

    async Task Open()
    {
        m_Environment = await CoreWebView2Environment.CreateAsync(null,
            Path.Combine(AppContext.BaseDirectory, "WebView2"));
        await m_View.EnsureCoreWebView2Async(m_Environment);
        m_View.CoreWebView2.WebMessageReceived += (_, message) => Receive(JsonNode.Parse(message.WebMessageAsJson)!);
        m_View.CoreWebView2.Navigate(m_Address.ToString());
    }

    // { type: "surface", id, bytes }：建（或沿用同大小的）画面区并交给网页；
    // { type: "surface-share", id, processId }：把画面区句柄复制进该进程，回报句柄值；
    // { type: "surface-release", id }：释放画面区；
    // { type: "surface-drawn", sequence, captured, written }：计时开关开着时网页每画完一帧的回报。
    void Receive(JsonNode message)
    {
        string? type = (string?)message["type"];
        if (type == "surface-drawn")
        {
            m_Timing?.Record(long.Parse((string)message["sequence"]!), long.Parse((string)message["captured"]!),
                long.Parse((string)message["written"]!));
            return;
        }
        if (type is not ("surface" or "surface-share" or "surface-release"))
            return;
        string id = (string)message["id"]!;
        try
        {
            switch (type)
            {
                case "surface":
                    Create(id, (ulong)message["bytes"]!);
                    break;
                case "surface-share":
                    int processId = (int)message["processId"]!;
                    Post(new JsonObject
                    {
                        ["type"] = "surface-share", ["id"] = id, ["processId"] = processId, ["handle"] = Share(id, processId)
                    });
                    break;
                case "surface-release":
                    if (m_Surfaces.Remove(id, out CoreWebView2SharedBuffer? released))
                        released.Dispose();
                    break;
            }
        }
        catch (Exception failure)
        {
            Post(new JsonObject { ["type"] = type, ["id"] = id, ["error"] = failure.Message });
        }
    }

    void Create(string id, ulong bytes)
    {
        if (!m_Surfaces.TryGetValue(id, out CoreWebView2SharedBuffer? buffer) || buffer.Size != bytes)
        {
            buffer?.Dispose();
            buffer = m_Environment!.CreateSharedBuffer(bytes);
            m_Surfaces[id] = buffer;
        }
        m_View.CoreWebView2.PostSharedBufferToScript(buffer, CoreWebView2SharedBufferAccess.ReadOnly,
            new JsonObject { ["type"] = "surface", ["id"] = id, ["bytes"] = bytes, ["timing"] = m_Timing != null }.ToJsonString());
    }

    long Share(string id, int processId)
    {
        if (!m_Surfaces.TryGetValue(id, out CoreWebView2SharedBuffer? buffer))
            throw new InvalidOperationException($"没有画面区 '{id}'。");
        using Process target = Process.GetProcessById(processId);
        if (!DuplicateHandle(GetCurrentProcess(), buffer.FileMappingHandle.DangerousGetHandle(), target.Handle,
                out IntPtr duplicated, 0, false, DuplicateSameAccess))
            throw new InvalidOperationException($"复制画面区句柄到进程 {processId} 失败，Win32 错误 {Marshal.GetLastWin32Error()}。");
        return duplicated.ToInt64();
    }

    void Post(JsonObject message)
    {
        if (m_View.CoreWebView2 != null)
            m_View.CoreWebView2.PostWebMessageAsJson(message.ToJsonString());
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr source, IntPtr targetProcess, out IntPtr target,
        uint access, bool inherit, uint options);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetCurrentProcess();
}
