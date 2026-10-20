# BTSMTL 网页工作台原型

通过独立 HTML 页面编辑通用编排图，并调用本仓库的 .NET 编译核心。工具只引用 `BTSMTL.Compilation` 及其编排、Timeline、Foundation 依赖，不需要 Unity Editor、3C 角色配置或 Photon SDK。

## 启动

需要支持构建并运行 .NET 8 的 SDK 与运行时。在仓库根目录运行：

```powershell
./Tools/BTSMTL.WebWorkbench/Start.ps1
```

浏览器打开 <http://127.0.0.1:5367>。脚本完成编译并关闭构建服务器后启动服务；终端中的 `Ctrl+C` 停止服务。使用 `-Port 5368` 可指定端口。服务只监听本机，临时目录位于本仓库 `.codex/tmp/system-temp/`。

HTML、CSS、JavaScript 修改后刷新页面即可看到结果；修改 C# 接口后重启服务以重新编译。

## 本版操作

- 从左侧添加节点，在画布拖动节点；先点击来源节点右侧端口，再点击目标左侧端口建立连接。新连接替换目标原父节点，Root 和 Branch 的同一出口也替换原连接。
- 右侧修改名称、布尔输入与子节点顺序。节点名称和布局只属于工作区；布尔值、节点类型和连线顺序进入核心编译。
- 点击“编译图”查看实际结果。支持 Root、Sequence、Selector、Parallel、Branch、ConditionResult、Succeed，使用树形 Child 连接和布尔常量输入。首屏示例会自动编译一次。
- 时间轴支持新增、删除、移动、右侧裁剪、属性改值、选择轨道和游标播放，按 0.05 秒吸附。它是独立编辑草稿，尚未转换为核心 Timeline 内容，播放也不是编排运行或执行记录。
- “保存草稿”写入当前浏览器的 localStorage。导出与导入使用 `btsmtl-web-workspace/1` 工作区 JSON，包含布局、图和时间草稿；它不是 BTSMTL 已发布内容格式，不导入 Unity 资产。
- `Ctrl+S` 保存，`Ctrl+Z` 撤销，`Ctrl+Shift+Z` 或 `Ctrl+Y` 重做，`Delete` 删除选中项。输入框保留原生编辑快捷键。滚轮缩放，拖动空白处平移。

## 接口与职责

| 接口 | 输入与实际处理 | 输出 |
| --- | --- | --- |
| `GET /api/core` | 从真实编译程序集和 `OrchestrationValuePortContracts` 读取本版开放操作与输入端口 | 程序集名称、节点合同版本、端口约束和可接受类型 |
| `POST /api/compile` | `GraphRequest` 提供入口、节点、布尔常量和有序连线；网页边界确认本版树形结构，然后调用 `BtsmtlGraphSemanticBuilder` 和 `BtsmtlOrchestrationGraphCompiler` | 编译摘要、来源修订、操作索引和真实诊断 |

`wwwroot/app.js` 持有尚未提交的工作区；`Program.cs` 提供同源 HTTP 与静态文件；`GraphCompiler.cs` 将本版编辑输入声明为现有语义操作。图调用路径、入口调用来源由该适配器按核心协议提供，核心负责端口类型、Root 子节点等编译规则。没有另写编排执行器。

编译产物仅存在于本次请求内存，接口返回摘要供界面核对，不写入正式发布资产。当前版本尚未接入运行执行、状态机编辑、值节点连线、黑板、引擎资源或 Timeline 发布。需要这些能力时应接原核心合同，不能将网页时间游标当作核心执行结果。

## 已检查范围

独立项目构建通过，无警告和错误。已在本机浏览器检查真实图编译、布尔值修改后的编译修订、节点添加／连接／删除／拖动／撤销、时间片段移动／裁剪／撤销、游标播放、草稿恢复及文件导入导出。断开的图显示网页结构错误，Root 多子节点保留核心的 `single_child_control_flow_invalid` 诊断。检查了 1600、1024 和 390 像素窗口；窄窗口中的图可缩放和平移。

这些检查覆盖本版编辑与编译操作，不表示核心运行、完整 BTSMTL 作者能力或其他引擎接入已经完成。
