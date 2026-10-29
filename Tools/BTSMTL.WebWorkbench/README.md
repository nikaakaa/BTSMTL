# BTSMTL 网页工作台

本地 .NET 服务提供预览、播放器画面与动画资源接口，网页前端由 `web/` 下的 React 工程构建到 `wwwroot/` 后作为静态文件提供。网页要直接编辑技能 JSON（Inspector、节点图、FSM、Timeline、黑板与输入方案，见 3C `add-shared-document-authoring-and-web-workbench` 第 9 节），当前前端尚未接入编辑，只查看运行结果与资源；技能读取、查询与保存接口由项目宿主提供（3C 见 `Tools/CharacterWorkbench/Host/Authoring/README.md`）。

## 环境

- .NET 8 SDK 与运行时。
- Node.js 22 及以上（附带 npm），仅构建期需要；`web/package-lock.json` 锁定全部依赖版本。

## 启动

在仓库根目录运行：

```powershell
./Tools/BTSMTL.WebWorkbench/Start.ps1
```

脚本依次在 `web/` 执行 `npm ci`、`npm run typecheck`、`npm run build`，再编译并启动服务；任一步失败即停止，不用旧产物启动。浏览器打开 <http://127.0.0.1:5367>，`Ctrl+C` 停止。`-Port` 指定端口，服务只监听本机；`-AnimationLibrary` 指定正式网页动画清单。npm 缓存位于本仓库 `.codex/tmp/web-workbench/npm-cache`，构建位于 `.codex/tmp/web-workbench/build`，进程临时目录为 `.codex/tmp/system-temp/`。

独立服务没有登记预览宿主与画面来源，预览接口返回 `available=false`；运行、帧与播放器画面需要项目宿主（3C 见 `Tools/CharacterWorkbench/README.md`）。

## 前端开发

| 命令（在 `web/` 下） | 作用 |
| --- | --- |
| `npm ci --ignore-scripts` | 按锁文件安装依赖到 `web/node_modules` |
| `npm run dev` | 开发服务 <http://127.0.0.1:5368>，`/api` 与 WebSocket 代理到 5367 上已运行的宿主，修改后热更新 |
| `npm run typecheck` | TypeScript 严格类型检查 |
| `npm run build` | 类型检查后构建到 `../wwwroot`，构建前清空；`wwwroot/` 不入库 |

技术栈：React 19、TypeScript、Vite、dockview（停靠）、@xyflow/react（节点图）、dnd-timeline（Timeline）、zustand（状态）、Base UI（基础控件）、three（3D 视图）。

| 目录 | 职责 |
| --- | --- |
| `src/shell/` | 停靠外壳、主工具栏、布局保存与弹出窗口、快捷键、地址同步、就绪标记、唯一主题文件 `theme.css` |
| `src/state/` | 全局 store、会话与播放、帧缓存、各面板数据模型、播放器连接 |
| `src/api/` | 宿主 HTTP、SSE 与 WebSocket 读取 |
| `src/panels/` | 各面板；`registry.tsx` 登记面板类型与组件 |
| `src/ui/` | 基础控件、组件头、空状态、身份折叠、虚拟列表 |
| `src/workers/` | 记录查询与格式化的 Worker |

## 面板

| 类型 id | 标题 | 内容 |
| --- | --- | --- |
| `player` | 画面 | 播放器推流画面，Game 视图式工具条、点选、输入转发与 Statistics 小窗 |
| `viewport` | 逻辑视图 | 当前帧的角色根、碰撞几何、扫掠与接触 |
| `resources` | 动画资源 | 动画清单、片段播放与时刻拖动 |
| `graph` | 节点图 | 当前帧的实际路线、已发生节点、经过连线与端口值 |
| `inspector` | 检查器 | 选中对象的字段，身份信息统一在底部折叠 |
| `frames` | 帧记录 | 当前帧或固定帧的分区记录 |
| `timeline` | Timeline | 运行 Timeline 与预览输入轨道、标尺、游标 |
| `query` | 记录查询 | 按条件查询整条运行的记录 |
| `compare` | 比较 | 两个运行逐帧比较 |
| `console` | 控制台 | 请求失败与运行消息 |
| `content` | 内容 | 场景、运行来源与输入方案 |

`viewport`、`graph`、`inspector`、`frames` 可开多个实例，实例 id 为"类型 id-序号"。布局保存在浏览器本地，主工具栏 Layout 菜单可恢复默认布局或打开面板。

## 外观与文字

主题变量只在 `src/shell/theme.css` 定义，外观照 Unity 编辑器：面板直接拼接、缝宽 2px，页签栏 22px，面板工具条 24px，正文 12px，数字等宽 11.5px。不使用带间距的圆角卡片、胶囊标签、图标底块、光晕、流动动画、渐变背景和装饰图标；状态写成普通字段。

- 面板空状态只用 `<Empty text="…" />`，一句话。
- 运行 ID、修订与哈希只在检查器底部 `<IdentityFold>` 显示。
- 悬停提示用 Base UI Tooltip，面板内不写说明段落。

## 地址与截图就绪标记

| 地址 | 含义 |
| --- | --- |
| `/?run=<id>&frame=<序号>&select=<种类:身份>&panel=<类型 id>` | 打开运行的第几帧（帧序号从 0 起），选中对象，激活该类型面板 |
| `/?panel=resources&resource=<身份>&time=<秒>` | 打开动画资源片段的指定时刻 |

页面加载时恢复地址，状态变化后以 `history.replaceState` 回写。`panel` 激活该类型第一个实例所在页签，没有实例时在默认位置打开一个；`select` 种类为 `node`、`edge`、`clip`、`track`、`actor`。

就绪标记写在 `<html>` 的 `data-*` 属性上，只有可见面板参与判断，帧或资源变化时先删除 `image-ready`：

- 运行：`data-preview-run`、`-frame`、`-frame-index`、`-consistency`、`-content`、`-rendered`、`-image-ready`。Timeline、节点图与 `panel` 指定的面板按当前帧实际绘制完成后一次写入；`preview-rendered` 由逻辑视图按实际画出的内容填写。
- 资源：`data-resource-identity`、`-revision`、`-library`、`-source`、`-time`、`-image-ready`，资源面板按指定时刻绘制后写入。

3C `Tools/CharacterWorkbench/CapturePreview.ps1` 用无头浏览器打开上述地址并核对这些属性。

## 接口与职责

`WorkbenchApplication.Create` 提供默认文件、静态文件与动画资源接口，不做未知路径回退。`PreviewApi` 消费项目实现的 `IPreviewHost`：来源与正式输入、开始／控制／取消、运行回执、帧分页、图定义、路线观察、SSE 与比较。`GET /api/preview/host` 返回可用状态与各预览档位声明。

`Contracts/PreviewObservationContracts.cs` 定义宿主无关的帧：无损文本帧号、运行相对秒数、Actor 根变换、通道及可选一致性值。根与几何采用右手坐标、Y 轴向上、米，旋转为 `[x,y,z,w]`；项目负责正式数值和坐标换算。`RawJson` 保留正式读取模型的无损 JSON 文本，页面不重新解析其中的大整数。

可选 `IPreviewImageSource` 提供画面来源、构建显示字段，以及按运行、来源作业和宿主帧读取的图片与元数据。`PreviewImageApi` 持有 `/api/preview/players`、`/runs/{runId}/player/{sourceId}/frames/{hostFrame}` 与常驻 `/live/{id}/view`。视频正文为 4 字节小端元数据长度、UTF-8 JSON 和图像字节；元数据提供无损帧号、尺寸、方向、媒体类型及适配公共坐标的列主序 `worldToClip`。

`AnimationResourceApi` 按清单哈希核对 glb 后按内容版本提供 `/api/resources/animations` 与分段文件读取。
