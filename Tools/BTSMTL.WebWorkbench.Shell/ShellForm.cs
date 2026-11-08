using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace BTSMTL.WebWorkbench.Shell;

// 工作台桌面壳：网页界面由 WebView2 显示；网页声明的画面区是盖在网页上的原生子窗口，
// 后端把自己的渲染窗口挂进去。壳只按网页报来的位置、可见性与浮层空洞摆放画面区，不认识具体引擎。
sealed class ShellForm : Form
{
    readonly Uri m_Address;
    readonly WebView2 m_View = new() { Dock = DockStyle.Fill };
    readonly Dictionary<string, NativeSurface> m_Surfaces = new(StringComparer.Ordinal);

    public ShellForm(Uri address)
    {
        m_Address = address;
        Text = "BTSMTL 工作台";
        ClientSize = new Size(1600, 960);
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(m_View);
        Load += async (_, _) => await Open();
        Activated += (_, _) => Activate(true);
        Deactivate += (_, _) => Activate(false);
    }

    async Task Open()
    {
        CoreWebView2Environment environment = await CoreWebView2Environment.CreateAsync(null,
            Path.Combine(AppContext.BaseDirectory, "WebView2"));
        await m_View.EnsureCoreWebView2Async(environment);
        m_View.CoreWebView2.WebMessageReceived += (_, message) => Receive(JsonNode.Parse(message.WebMessageAsJson)!);
        m_View.CoreWebView2.Navigate(m_Address.ToString());
    }

    // 网页消息 { type: "surface", id, visible, rect, holes }；rect 与 holes 为 WebView 客户区设备像素。
    void Receive(JsonNode message)
    {
        if ((string?)message["type"] != "surface")
            return;
        string id = (string)message["id"]!;
        if (!m_Surfaces.TryGetValue(id, out NativeSurface? surface))
        {
            surface = new NativeSurface { Visible = false };
            Controls.Add(surface);
            surface.BringToFront();
            m_Surfaces.Add(id, surface);
        }
        if ((bool?)message["query"] == true)
            Post(new JsonObject { ["type"] = "surface", ["id"] = id, ["handle"] = surface.Handle.ToInt64() });
        if (message["rect"] is JsonNode rect)
            surface.Place(Rect(rect), message["holes"]?.AsArray().Select(hole => Rect(hole!)).ToArray() ?? []);
        surface.Visible = (bool?)message["visible"] == true;
    }

    static Rectangle Rect(JsonNode value) => Rectangle.FromLTRB(
        (int)Math.Round((double)value["left"]!), (int)Math.Round((double)value["top"]!),
        (int)Math.Round((double)value["right"]!), (int)Math.Round((double)value["bottom"]!));

    void Post(JsonObject message)
    {
        if (m_View.CoreWebView2 != null)
            m_View.CoreWebView2.PostWebMessageAsJson(message.ToJsonString());
    }

    void Activate(bool active)
    {
        foreach (NativeSurface surface in m_Surfaces.Values)
            surface.Activate(active);
    }
}
