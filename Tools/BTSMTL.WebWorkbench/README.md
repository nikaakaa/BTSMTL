# BTSMTL 网页作者工作台

HTML 和 CLI 从本地服务读取项目正式作者目录。共同库持有根、图、稳定引用、无损作者值与图修改候选；页面不再消费原型 localStorage，也不维护七节点清单或独立时间草稿。原浏览器草稿及已导出的原型文件保留。

## 启动

需要支持构建并运行 .NET 8 的 SDK 与运行时。在仓库根目录运行：

```powershell
./Tools/BTSMTL.WebWorkbench/Start.ps1 -WorkspaceRoot D:/Unity_Project_1/3C/Authoring/BTSMTL/Documents
```

`WorkspaceRoot` 必须明确指定已有的项目作者目录。浏览器打开 <http://127.0.0.1:5367>。脚本完成编译并关闭构建服务器后启动服务，`Ctrl+C` 停止。`-Port` 指定端口；服务只监听本机。构建位于本仓库 `.codex/tmp/web-workbench/build`，进程临时目录为 `.codex/tmp/system-temp/`。

`-AnimationLibrary` 指定正式网页动画清单。打开 `/resources.html` 可选择清单内片段、播放、拖动时间及调整模型视角。three.js 及其加载器使用锁定的本地 npm 依赖；启动脚本通过 `npm ci` 还原。服务先按清单哈希核对 glb，再按内容版本提供分段读取；页面明确显示资源版本与近似表现范围。动画资源页尚未接到真实会话的帧和播放请求。

HTML、CSS、JavaScript 修改后刷新页面即可看到结果；修改 C# 接口后重启服务以重新编译。

## 当前操作

选择内容根及其文件，查看实际图拓扑、作者字段、引用、源文和读取修订。画布支持平移、缩放、节点及连接选择；点击输出端口再选输入可提交连接候选，也可从工具栏创建节点与连接。转移线属性显示文档中的实际条件、优先级及中断字段，同一输出可整组重排。归属路径与引用跳转保留稳定对象身份，可从 StateBody 或条件图返回父图；URL可定位具体节点或连接。

字段按原定义编辑，当前根已使用的同类型稳定引用可按文档或资源名称选择，复合集合可逐项编辑、复制、删除及重排。变量页修改原所属图的默认值、作用域、生命周期与输入绑定，可复制成新变量或删除声明。文件列表明确标注未保存候选；服务断开、请求失败和过期基线会显示具体原因并保留候选。非法外部根可查看原始字节对应的 UTF-8 原文，服务不恢复旧缓存覆盖文件。

`DocumentEditSession` 将字段、节点、连线、变量及 Timeline 操作合入同一正式文档候选，请求只走 `/api/authoring/documents/*`。切换根或重读前必须处理当前候选；请求或成功后的回读中断时保留原候选及请求身份，沿原身份查询。保存后的撤销／重做由共同反向候选合并后续差量或报告冲突，页面不回写旧文件快照。当前项目领域接收与保存装配尚未接通；页面据实保留失败和未完成阶段，不代表内容已保存或被游戏采用。

## 接口与职责

| 接口 | 输入与实际处理 | 输出 |
| --- | --- | --- |
| `GET /api/workspace` | 启动参数指定的目录 | 实际作者目录和文档格式版本 |
| `GET /api/workspace/roots?offset=0&limit=100` | 读取根清单，分页上限 200 | 稳定身份、根修订、直接依赖及文件问题 |
| `GET /api/workspace/root?path=Root/root.json` | 按根的明确文件清单读取 | 根正文、全部文件原文及实际字节修订 |
| `GET /api/workspace/source?path=Root/root.json` | 读取限定目录内的精确文件 | 原文、路径、SHA-256，不要求文件已合法 |
| `GET /api/workspace/references?rootId=ID&offset=0&limit=100` | 读取根的正式外部引用 | 分页反向引用 |

`AuthoringWorkspaceDirectory` 负责文件范围和读取；`WorkspaceApi` 只投影 HTTP 协议；`wwwroot/documents.js` 展示同一内容。根内文件路径不能越出 owner 目录；布局与业务文档分别持有。64 位数值和定点原值在作者值中使用文本，不经过 JavaScript Number。

候选绑定整个根的原始文件修订。共同文档服务接收领域结果后才允许提交，过期请求返回冲突路径、当前修订和相关对象；共同文件事务处理创建、替换、删除及失败恢复。`AuthoringDocumentApi` 已归共同工作台，图／Timeline请求转换也使用共同实现；项目启动尚未登记正式领域接收器，真实保存和跨端撤销尚未验证。

`Contracts/BTSMTL.WebWorkbench.Contracts.csproj` 持有唯一的图修改、文档候选、Timeline 修改、端口及结构校验请求，独立于 ASP.NET 和项目运行库。Host、CLI、MCP 引用同一程序集，保留原多态 `kind`、必需字段和无损时间原值。`AuthoringDefinitionApi` 负责定义、能力、端口及结构校验的公共路由，项目通过 `IAuthoringDefinitionQueries` 提供实际描述与校验；`AuthoringPortProjection`、`AuthoringTimelineEditTransport` 复用原共同投影和修改器。

公共 `POST /api/authoring/graphs/edits/preview` 仍供 AI 客户端执行结构预览，由项目原修改实现返回 `graph-edit-preview`、`saveReady=false`、`acceptance=not-received`；网页已不消费该入口。它不提供正式保存许可。迁移后实际 HTTP 端口投影、FSM／流图校验和 CLI 结构预览通过，磁盘根修订保持不变。最新 Host 构建0错误、6个既有警告，CLI／MCP 构建0错误、0警告；本轮 MCP stdio 协议运行检查被自动审批以 `blocked by policy` 拒绝，未执行。历史 MCP 工具发现只代表当时版本，最新证据归项目 README。

原 `GraphAuthoringGeneratedContract`、成员绑定和字段描述已纳入普通 .NET 作者库。字段类型来自原成员，资源绑定回调使用所属平台的实际资源类型；Unity Inspector 在平台边界转换原对象。生成器和网页服务本轮构建均为 0 警告、0 错误；这一步尚未迁出完整技能节点及领域接收，不代表网页已可修改技能。

原能力目录、端口投影及编排声明直接进入普通 .NET 作者工程。RGBA 颜色由共同值持有，仅 Unity 适配器转换为原生颜色。公共 `/api/authoring/definitions`、`/definitions/document`、有界 `/capabilities`、精确 `/capabilities/{id}` 和 `POST /ports` 只消费项目登记的描述来源；当前3C `AuthoringDefinitionCatalog` 仍读取原导出文件，正式节点来源合同尚由共同基础接续，不能把手工导出当公共模块的使用前提。网页只显示共同投影器返回的固定、字段派生及作者动态端口。变量端口直接引用正式 `valueType` 字段，目标读取与捕获保留原固定端口。实际原导出的117种能力覆盖249张图、1379个节点，2299个端口投影的问题数为0。

共同作者值格式 2 的类型身份不包含 CLR 核心库名或程序集版本，保留基础类型、递归泛型／数组形状和业务程序集名。3C Editor 已实际采用 Single `G9`／Double `G17`，重新导出的 249 个图与 12 个 Timeline 在普通 .NET 文本回读全部一致；33 个变量声明及四类基础默认值的恢复证据保留。正式作者工作区为空，完整技能的创建、修改、领域接收和保存入口仍未接通。

共同 `AuthoringStateMachineContract` 接收普通状态与转移快照，也接收正式图文档及引用解析器。3C Native 适配与 `POST /api/authoring/state-machines/validate` 消费同一规则；原状态字段、系统锚点及连接容量生成对应能力声明。共同库与 Host 编译通过，目标 Editor 已采用并完成正式导出，Corin FSM 的 15 个状态、42 条转移和实际图引用已接收通过。页面显示同一回执，失败仍保留原图；FSM 接收不表示完整技能已保存或发布。JavaScript 语法检查通过，本轮无头浏览器截图被自动审批拒绝，视觉检查尚未完成。

普通流图由共同 `AuthoringFlowGraphContract` 检查页面允许节点、固定入口、连线类型容量与环路。3C Native 原入口和 `POST /api/authoring/graphs/validate` 共用实现，页面显示对应结构接收结果；错误由作者 HTTP 边界返回 422、请求身份和具体原因。目标 Editor 已采用并导出正式页面入口，Corin 技能的 248 张普通流图全部通过，额外输入方案的 2 张图也通过；此阶段不接收尚未迁出的全部字段与根业务规则，不开放不完整的保存入口。共同作者库为 0 警告、0 错误；最新项目 Host 为 0 错误、5 个既有警告。

3C 原迁移入口现可将独立输入方案资产及其私有输入时间轴、片段图和 Marker 图保存为另一份共同作者根。实际候选保留原资产 GUID、`Owner=ability:Attack`、方案参数与当前选择，不改变 Unity 资产来源。项目工作台已读取 1 个方案、1 条输入 Timeline、2 张图的 5 个文件；输入图通过同一结构检查。项目 CLI 和 MCP 的文档查询显式接受 `Workspace`／`Migrations` 来源，图检查也调用相同 HTTP 服务。CLI、MCP 均构建通过，0 警告、0 错误；22 项 MCP 工具已发现，真实图检查回执已与 CLI 对应。251 张图、13 条 Timeline／输入时间轴及 2 份根文档在 .NET 10.0.11 的文本回读无差异，具体入口、原始回执与未完成范围归项目 `Tools/CharacterWorkbench/README.md`。

`WorkbenchApplication.Create` 装配共同页面，`PreviewApi` 消费项目实现的 `IPreviewHost`：来源与正式输入、开始／控制／取消、运行回执、帧分页、图定义、路线观察、SSE 和原比较器均进入所属宿主。`GET /api/preview/host` 返回可用状态、帧显示名、筛选、停止位置及各预览档位的可用声明；独立服务未登记宿主时返回 `available=false`。网页沿原请求身份恢复结果，历史帧查看不推进会话。

`preview.html` 按 Unity Timeline 的位置组织预览：单行放档位、播放、逐帧、时刻、状态和场景，输入方案入口位于“预览输入”组头，节点图显示当前调用的实际路线及有明确作者来源的端口值，端口继承页面 12px 正常字号。时间标尺直接拖动，聚焦后支持左右键及 Home／End；唯一帧序号同时驱动帧读取、时间游标与节点观察。“选择运行”仅作不可选的提示项；状态悬停提供完整文字及错误。连接、完整调用身份、能力缺项和原始记录按需查看，不另设页签、底栏或重复滑条。图调用列表的序号仅用于选择，完整运行身份仍保存在选项值与悬停信息中。

当前 Host 未提供输入方案及按键轨，`快`档也明确不可用；方案按钮与对应档位保持禁用。图定义仍缺作者名称、坐标和数值连线，页面只消费正式编译定义并按控制连线排列，不将其称为 Unity 作者布局，也不读取源 JSON 补造输入或名称。正式无头入口为 3C `Tools/CharacterWorkbench/CapturePreview.ps1`；1600×1050 截图的页面版本为 `0276b1d`，采用运行 `aaf63465396440f4b91c03e7976501c0` 的第 110 帧，游标序号 50、一致性值 `1efb40f515b77ede`，显示 2 个实际经过节点和 1 条选中连线。图片及元数据位于 `D:/Unity_Project_1/3C/.codex/tmp/cw-preview-c-20261028/timeline-unity-row-1600.*`。后续暂停状态、字号、提示项及悬停修正已通过 JavaScript 语法和 diff 检查，新截图等待正式 Host 实例交接；旧截图不代替这些修正的运行检查，也不代替交互与端到端验收。

`Contracts/PreviewObservationContracts.cs` 定义宿主无关的帧：无损文本帧号、运行相对秒数、Actor 根变换、通道及可选一致性值。根与几何采用右手坐标、Y轴向上、米，旋转为 `[x,y,z,w]`；项目负责正式数值和坐标换算。宿主专有字段作为带显示名的字段及区块提供，作者定位直接携带文档／元素身份。`RawJson` 保留正式读取模型的无损 JSON 文本，页面不重新解析其中的大整数；它不承诺与磁盘原文件字节或字段集合相同。公共 `preview`、`player`、`logic-viewport` 和观察查询不读取项目动作、SDK、FP或Roster字段。

可选 `IPreviewImageSource` 交付画面来源、构建显示字段，以及按运行、来源作业和宿主帧读取的图片与元数据。`PreviewImageApi` 接管原 `/api/preview/players`、`/runs/{runId}/player/{sourceId}/frames/{hostFrame}` 和常驻 `/live/{id}/view`，不保留第二个同义图片入口。视频正文为4字节小端元数据长度、UTF-8 JSON和图像字节；元数据提供无损帧号、尺寸、方向、媒体类型及适配公共坐标的列主序 `worldToClip`。独立服务未登记画面来源时明确返回不可用。

3C已实现 `QuantumPreviewHost`、`QuantumPreviewProjection` 和 `UnityPreviewImageSource`，保留原模拟、存档、比较、播放器进程及命令路径。181帧历史真实记录的帧号、时钟、根、一致性值及全部既有字段已通过HTTP逐值核对，分页与SSE一致，原请求恢复返回原结果且正式文件未变；旧模型额外补出的两处空字段及首次检查失败均保留证据。新状态／变量／几何通道、真实画面协议、空点选修复及浏览器交互仍须实际验证，输入Timeline自动换算及正式作者保存也未完成。版本、步骤与证据统一见3C `Tools/CharacterWorkbench/README.md`。

## 已检查范围

共同模型、确定性读写、修改候选、修订提交与网页服务已在普通 .NET 构建通过，0 警告、0 错误；JavaScript 语法检查通过。共同文件事务已通过临时文件实际创建、替换、删除、退役空目录及中途失败恢复检查；这不代替正式作者保存链验收。107 段动画清单与 glb 分段读取已通过实际 HTTP 检查。项目宿主的迁移候选 Timeline、变量页与暂停预览已实际渲染截图；候选仍为只读，变量声明已改用原 owner 并由 Editor 重新导出、普通 .NET 回读通过。未进行正式技能来源切换和用户验收。旧原型的交互检查属于已删除的旧入口，不沿用为新页面的验证结果。

独立作者值读取曾把日期形字符串自动转成日期 token，导致原字符串不能回读。现与完整文档共用禁用日期转换的解析边界，实际写入、回读 `2026-10-26T00:00:00Z` 保持文本相同；重复键和额外 JSON 对象由同一解析器拒绝。
