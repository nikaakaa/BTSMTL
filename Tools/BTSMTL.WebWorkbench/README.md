# BTSMTL 网页工作台

独立工作台在浏览器中读写本机的 BTSMTL 图、Timeline 和变量声明。项目宿主可以复用同一套网页与文档服务，提供项目节点、FSM、资源、发布和运行预览。前后端接入方式见[接口说明](API.md)。

## 环境

- .NET 8 SDK 与运行时。
- Node.js 22.12 或更高版本（附带 npm）。网页构建和 DependencyGuard 使用 `web/package-lock.json` 锁定的依赖。
- PowerShell，用于运行下方启动脚本。

## 启动

在仓库根目录运行以下命令。

```powershell
./Tools/BTSMTL.WebWorkbench/Start.ps1
```

脚本依次安装锁定的网页依赖、检查 TypeScript、构建网页和 .NET 服务，全部成功后监听本机端口。浏览器打开 [本地工作台](http://127.0.0.1:5367)，终端中按 `Ctrl+C` 停止。`-Port` 指定端口，`-AnimationLibrary` 传入正式网页动画清单。npm 缓存与 .NET 构建产物位于 `.codex/tmp/web-workbench/`。

默认工作区是 [Examples](Examples)。保存会写回这里的 JSON。需要保留原样时，把 `Examples` 复制到自己的目录，再用 `-Workspace <路径>` 打开副本。工作区文档由 `Graphs/<目录>/graph.json`、`Timelines/<目录>/timeline.json` 和 `Variables/<目录>/variables.json` 发现。

1. 在 Project 中双击「条件分支示例」，点选「比较数值」，在检查器里改 `a`。也可以右击节点图空白处，选择「添加节点…」并加入 `FloatMultiply`。点「保存」后重新打开文档，查看已保存的节点和输入。
2. 在 Project 中双击「片段时间示例」，选中时间轴片段，在检查器的「时间」中调整起点或终点，再保存。
3. 打开「条件分支示例」，点节点图工具栏的「黑板」，选 `float` 并添加变量，再点「保存」。变量保存在 `Variables/example-graph-variables/variables.json`。

独立模式提供核心流程和数值节点、树轨及已有片段时间、变量声明的编辑。新建整份文档、项目资源引用、编译发布、运行输入和 Player 预览由项目宿主提供。

组合项目宿主时，`MapAuthoringApi(documents, resources)` 挂载文档、目录、变量、引用替换和资源查询，另以 `MapAuthoringApi(contracts)`、`MapPublicationApi(publication)` 与 `MapPreviewInputApi(inputs)` 挂载合同、发布和输入操作。`MapPlayerApi(adapter, options)` 创建唯一的播放器队列、常驻池、Live、内容刷新和代码更新服务，返回 `Jobs/Live/Refreshes` 供组合；配方、启动参数、输入与原生位置解释来自项目适配。公共服务保存规范 JSON，不解释项目参数。适配接口位于不依赖 ASP.NET 的 `Authoring/`，公共请求位于 `Contracts/`，执行位于 Host 库的 `Player/`。`MapPreviewQuickApi(adapter, players?)` 另装配公共快速路线服务：输入日程、静态查询、推导、投影、定位、改时、条件发布与快档播放器调度位于 `Quick/`。`IPreviewQuickAdapter` 只提供语义编译结果、领域状态与输入换算、项目运行包。`IPreviewHost` 只提供宿主能力、来源及输入声明，`MapPreviewApi(host, players?)` 回报播放器能力。没有播放器适配或包体时，纯路线正常完成，画面、真实运行与代码更新入口隐藏；网页不向缺失的播放器路由轮询。

## 文档与入口

项目树从 `documents/types` 读取类型和发现规则，以“文档 → 图／Timeline”展开。入口目录的 `isDefault` 决定默认入口；`entryGraph`、`transitionOrder`、`timelineOwnership` 与 `displayName` 由项目适配映射到正式存储成员。通用网页不按项目字段或根文件名推断职责。新建文档从创建目录获取根类型与初始配置，直接使用通用字段检查器编辑，返回的草稿仍经同一文档保存入口落盘。

快档的路线段携带 `conditionDependencies`，区分输入方案写入和默认读取。真实档对比复用已有帧缓存，显示读取范围、实际状态进出与该段条件依据；来源或输入修订变化时标明过期。静态路线与真实运行记录分别保持原来源。

打开检查对象不会清空已选预览根；图和 Timeline 的“预览此图／此 Timeline”按钮明确切换入口。输入方案可从能力文档、独立图、独立 Timeline 或所选场景／角色创建，快速栏可直接打开所选方案继续加轨。路线视图同时显示输入轨和一组实际播放的 Timeline，按全局时间对齐，重复播放保留各自区段；跨文档区段打开正式所属文档后编辑。

快速栏真实运行使用同一作者方案的稳定身份，刷新目录后取得最新修订与实际方案下标，并沿原保存、编译、发布、采用和封存链执行。默认输入可回到其实际作者方案；多份根输入需要明确选择。没有作者方案、未确认载入场景或采用失败时显示原原因，实际运行结果从所选 Player 的归档读取。

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
| `src/api/` | 作者、Player 与档案 HTTP，以及画面 WebSocket 的类型和读取入口 |
| `src/panels/` | 各面板；`registry.tsx` 登记内置与宿主扩展面板的组件 |
| `src/extensions.ts`、`src/sdk.ts` | 宿主扩展登记（停靠面板、画面工具、图检查器）与扩展导入入口 `@btsmtl/workbench` |
| `src/ui/` | 基础控件、组件头、空状态、身份折叠、虚拟列表 |
| `src/workers/` | 记录查询与格式化的 Worker |

## 面板

| 类型 id | 标题 | 内容 |
| --- | --- | --- |
| `player` | 画面 | 播放器推流画面，Game 视图式工具条、点选、输入转发与 Statistics 小窗 |
| `viewport` | 逻辑视图 | 当前帧的角色根、碰撞几何、扫掠与接触 |
| `resources` | Project | 内容（按类型列出的根文档及其图／Timeline）、场景（各 Unity 场景的当前发布按名字列出，旧修订与临时包收进历史）、动画资源；选中对象的详情与运行设置在检查器 |
| `graph` | 节点图 | 当前帧的实际路线、已发生节点、经过连线与端口值 |
| `inspector` | 检查器 | 选中对象的字段，身份信息统一在底部折叠 |
| `frames` | 帧记录 | 当前帧或固定帧的分区记录 |
| `timeline` | Timeline | 作者 Timeline、预览输入轨道、标尺、游标；快档／真档默认看输入方案的路线视图（输入轨、路线动态轨与按全局时间摆开的各次播放） |
| `query` | 记录查询 | 按条件查询整条运行的记录 |
| `export` | 内容导出 | 当前内容的导出操作与结果 |
| `console` | 控制台 | 请求失败与运行消息 |

`viewport`、`graph`、`inspector`、`frames` 可开多个实例，实例 id 为"类型 id-序号"。宿主项目面板不在本表：宿主构建时经 `BTSMTL_WORKBENCH_EXTENSIONS` 给出扩展入口，面板类型追加在内置面板之后，画面工具显示在画面工具条；未给出时工作台不含任何宿主面板。布局保存在浏览器本地，主工具栏 Layout 菜单可恢复默认布局或打开面板。

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

- 运行：`data-preview-run`、`-frame`、`-frame-index`、`-consistency`、`-content`、`-rendered`、`-image-ready`。帧号与可选的一致性值直接取当前通用帧的 `hostFrame`、`consistency.value`，不要求逻辑通道；未提供一致性值时不写 `preview-consistency`。Timeline、节点图与 `panel` 指定的面板按当前帧实际绘制完成后一次写入；`preview-rendered` 由逻辑视图按实际画出的内容填写。
- 资源：`data-resource-identity`、`-revision`、`-library`、`-source`、`-time`、`-image-ready`，资源面板按指定时刻绘制后写入。

运行按实际已记录的来源分别报告 `data-preview-execution-kind`、`-execution-identity`、`-player-build`、`-editor-assembly-mvid`；各项为空时不写对应属性。3C `Tools/CharacterWorkbench/CapturePreview.ps1` 用无头浏览器打开上述地址并核对其所需属性。

## 接口与职责

`WorkbenchApplication.Create` 提供默认文件、静态文件与动画资源接口。作者文档、合同查询、资源、编译发布和预览通过对应适配接入；公共 `Player/` 持有作业、Live、代码更新与内容刷新，`Quick/` 持有静态路线、输入读取和快档调度。项目提供实际领域编译、输入换算、资源及不透明的运行值。逐操作请求、阶段与错误处理见[前端接口](API.md)。

快速路线按当前根文档和同一编辑视图捕获来源。入口为图或独立 Timeline，默认入口由适配标记。路线段显示条件写入和默认读取依据，内容与输入变化使旧结果过期；实际记录使用原输入日程并与静态计划并排比较。完整合同见[快速路线与快档画面](API.md#快速路线与快档画面)，源码、构建、HTTP 回读及实际运行范围见项目接入记录。

`Contracts/PreviewObservationContracts.cs` 定义宿主无关的帧：无损文本帧号、运行相对秒数、Actor 根变换、通道及可选一致性值。根与几何采用右手坐标、Y 轴向上、米，旋转为 `[x,y,z,w]`；项目负责正式数值和坐标换算。`RawJson` 保留正式读取模型的无损 JSON 文本，页面不重新解析其中的大整数。

可选 `IPreviewImageSource` 提供画面来源、构建显示字段，以及按正式作业和实际图像索引读取的图片。`PreviewImageApi` 持有 `/api/preview/players`、`/api/preview/player-jobs/{jobId}/frame-images/{imageIndex}` 与常驻 `/api/preview/live/{id}/view`。网页视频消息为 4 字节小端元数据长度和 UTF-8 JSON，像素经桌面壳共享画面区传递；元数据提供实际采样来源与时钟、尺寸、方向、媒体类型及适配公共坐标的列主序 `worldToClip`。

`AnimationResourceApi` 按清单哈希核对 glb 后按内容版本提供 `/api/resources/animations` 与分段文件读取。

## 构建守护

正式解决方案构建先执行 DependencyGuard。除项目类型名检查，还检查通用网页使用的 API 是否在工作台注册，以及 `m_` 字段是否由核心包正式声明。首次构建 SDK 前在 `web/` 执行 `npm ci --ignore-scripts`，安装锁定的 Babel 解析器；不读取注释中的示例，也不扫描项目扩展目录。缺少解析器会明确失败。规则及报告见 [依赖守护](../DependencyGuard/README.md)。
