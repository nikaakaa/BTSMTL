# Web Workbench 前端接口

本文按实际界面操作说明 HTTP、WebSocket 和数据合同。BTSMTL 工作台挂载作者文档、合同查询、资源、编译发布、输入方案和播放器接口，持有播放器队列、常驻进程池、实时会话、内容刷新、画面传输及代码更新。BTSMTL 同时持有快速路线的输入日程、静态查询、路线推导、投影及快档播放器调度；项目适配解释作者合同、资源、输入、运行包和原生播放器值。UI 负责布局、手势和同一正式文档的本地编辑事务。

本文对应当前源码合同。正在运行的 Host、Player 和静态前端必须采用相应版本；接口说明和源码存在不表示当前端口已采用。部署与实际检查结果见 [3C 作者接口记录](../../../3C/Tools/CharacterWorkbench/Host/Authoring/README.md)。

## 接入与公共规则

- HTTP 使用相对路径 `/api/...`，JSON 请求使用 `Content-Type: application/json`，文本使用 UTF-8。HTTP 外层属性名为 camelCase，作者原文及节点成员保持正式存储命名（如 `AuthoringId`、`ID`、`m_...`）。本文路径参数用 `{name}` 表示，实际拼入时编码。
- WebSocket 使用同源的 `ws://` 或 `wss://`。Vite 当前把 `/api`（含 WebSocket）代理到 `http://127.0.0.1:5367`；联调其它端口时，UI 开发配置应指向该实际 Host。
- 独立启动通用网页服务只装配静态页面、动画资源及不可用的 `/api/preview/host`；完整编辑与运行由项目 Host 注册适配，不再使用 `SkillAuthoring` 开关。
- 作者文件的 `content` 是完整 JSON 原文字符串。读取、编辑和写出沿用 [skillJson.ts](web/src/state/skillJson.ts)，保留大整数及 `1.0` 等正式浮点写法；不能经过普通 `JSON.parse`／`JSON.stringify` 整体往返。
- 公共帧键、64 位身份、序号、FP Raw 和修订保持字符串。定位请求只提交 `position:{hostFrame:"目标键"}`，由原 Host 按实际采用内容解释；网页不转换为 SDK 帧或 Timeline tick。
- 查询分页有两种原合同：作者查询用 `hasMore`，档案分页用 `nextOffset`（终点为 `null`）。按各自回执翻页，不把二者混用。
- 三维公共几何采用右手坐标、Y 轴向上、米；四元数顺序为 `[x,y,z,w]`。画面使用实际 `flipY` 和列主序 `worldToClip`。

## 分层与适配接口

依赖只能从上往下：项目 Host → 项目适配 → BTSMTL 工作台（Host 库、Contracts、Authoring、网页）→ BTSMTL 核心。工作台源码与网页不出现项目类型名、SDK 名和项目字段名，由 DependencyGuard 在构建期检查项目名称、通用网页路由和作者字段归属。项目只经以下接口接入：

| 接口 | 所在工程 | 项目提供 | 工作台挂载 |
| --- | --- | --- | --- |
| `IAuthoringDocumentAdapter` | `Authoring` | 文档类型与发现规则、根类型、默认入口、语义角色、文档内元素、存储形状、保存前规范化、变量类型候选 | `MapAuthoringApi(documents, resources)`：文档类型与目录、正文、编辑视图、保存、变量、适配者目录、引用替换 |
| `IAuthoringContractAdapter` | `Authoring` | 类型、节点、字段、端口、Profile、引用、黑板、元素、曲线、工程、文档和应用查询 | `MapAuthoringApi(contracts)` |
| `IAuthoringResourceAdapter` | `Authoring` | 带领域的资源引用、资源作用域与文件子对象候选 | `MapAuthoringApi(documents, resources)` |
| `IAuthoringPublicationAdapter` | `Authoring` | 文档创建、删除、导出、发布范围与实际编译发布 | `MapPublicationApi(publication)` |
| `IPreviewInputAdapter` | `Authoring` | 输入片段、新建方案与录制导入 | `MapPreviewInputApi(inputs)` |
| `IPlayerAdapter` | `Authoring` | 包体、启动参数、配方、项目参数、原生位置与时钟解释、归档合同和消息投影 | `MapPlayerApi(adapter, options)`：作业、Live、代码更新与内容刷新；返回 `Jobs/Live/Refreshes` 供组合 |
| `IPreviewQuickAdapter` | `Authoring` | 文档入口的语义 IR、声明默认值、Timeline、状态键、按键换算和快档运行包 | `MapPreviewQuickApi(adapter, players?)`：推导、作业、定位、改时、条件发布与播放器调度 |
| `IPreviewHost` | Host 库 | 宿主描述、来源、输入声明 | `MapPreviewApi(host, players?)`；不承担 quick 流程，并回报播放器实际可用性 |
| `IPreviewArchive` | Host 库 | 归档读取，项目专有内容以 `PreviewField` 给出 | `MapPreviewArchiveApi(archive)`：`/api/preview/player-jobs/{id}/` 下的 archive、summary、geometry、source-map、frames、observations、logic、pose-layout、presentation、camera、input、curves、unavailable、frame-images |

上述接口按文档、合同查询、资源、编译发布、预览五组职责接入。规范 JSON 文本由 `BTSMTL.Authoring.Workspace.AuthoringJsonText` 提供，播放器的项目参数、内容身份与请求据此判等和存档。Project 文件浏览由 `MapProjectFolderApi(roots)` 挂载，项目仅提供正式根目录、挂载和文件身份。3C 专有的能力运行配置、扶墙和 MCP 诊断接口仍由项目 Host 挂载，网页专有界面由项目扩展入口登记。

字段的可调用 TypeScript 入口在 [api/authoring.ts](web/src/api/authoring.ts)、[api/preview.ts](web/src/api/preview.ts)、[api/player.ts](web/src/api/player.ts)、[api/resources.ts](web/src/api/resources.ts)。运行数据的解析器及导出类型在 [api/playerRecords.ts](web/src/api/playerRecords.ts)，图、调用、画面和资源类型在 [api/types.ts](web/src/api/types.ts)。这些文件供 UI 直接复用，不另写一套同名 DTO 或协议解析器。

## 界面操作与接口分工

| 界面操作 | 宿主提供的输入或处理 | UI 处理及输出 |
| --- | --- | --- |
| 打开根文档、FSM、子图或私有 Timeline | 文件快照、`document` 展开结果、图及元素稳定身份 | 维护当前文档、图实例路径和选择，渲染画布 |
| 添加节点、状态、转移、轨道或片段 | `queries/types`、`queries/type`、`queries/elements` 的正式类型、默认值、字段和端口 | 使用返回模板构造本地草稿；新增节点和状态自动选中交给检查器，不复制 C# 默认值或维护节点白名单 |
| Inspector 改字段、动态端口或引用 | 按当前未保存 `configuration` 描述字段、条件显示和端口；引用与黑板查询 | 显示对应控件，将结果写回原作者成员路径，并重查受影响的端口 |
| 拖节点、拉片段、框选、连线、复制删除、撤销重做 | 正式身份、作者文件与保存入口 | 手势及本地事务；节点位置和内容改回对应 JSON，统一保存。流图连线换接端点保留原连线 UID，换源时迁到新源节点的 outConnections，沿原事务撤销。每次鼠标移动无需 HTTP 请求 |
| 新建场景输入方案、添加按住或单按输入 | 场景角色及按键声明、`inputs/plans`、`inputs/fragments` | 选择实际角色和按键，编辑返回的方案／轨道／片段／图，保存整个变更闭包 |
| 保存并采用 | 文件写入、编译状态、输入修订、Player 刷新或作业回执 | 分别显示保存、编译、采用和作业阶段，按原状态地址继续读取 |
| 播放、暂停、逐帧、跳转、相机和试玩输入 | Live、内容载入、命令及真实 ACK、画面 WS | 管理控件状态；按实际内容时钟显示，按采样身份接收画面 |
| 回看结果、检查调用、端口与曲线 | 同一作业的分页档案、完整实际调用身份、可用性和原始值 | 按同一来源关联面板，保留缺失或不完整原因 |
| 动画资源及专用调参 | 动画清单／版本文件；已登记扩展的读写与命令合同 | 按元数据展示，不在 UI 实现运行算法 |

## 来源、能力与输入声明

| 方法与路径 | 参数 | 响应及用途 |
| --- | --- | --- |
| `GET /api/preview/host` | 无 | `{available,description}`；description 提供 `filters`、`inputLimit`、`player{available,reason}` 和 `modes`，各模式含 `available/reason/createsRun/defaultChannels` |
| `GET /api/preview/sources` | 无 | `Sources`：`scenes[{path,revision}]`、`quickScenes[{publication:{path,revision},scenePath,sceneGuid,sceneRevision}]`、`timelines`、`replays` 及正式目录 |
| `GET /api/preview/inputs` | `scene,revision` | `InputCatalog`：`revision,tickRate,actorTargets,localPlanRangeBasis,players`；各角色含 `localPlans,keys,values,requests` |
| `GET /api/preview/images` | 无 | `{available,description}`；图片适配能力 |
| `GET /api/preview/players` | 无 | `{items:PlayerBuild[]}`；`buildId,displayName,fields,rawJson` |

`scenes` 的 `path` 与 `revision` 成对传递。`timelines` 每项含可直接用于 Load 的 `content`，以及 `contentIdentity/contentRevision/bindingRevision/tickRate/scene/bindings`；不能把它改装成场景输入来源。

输入选项使用宿主实际返回的 `actorId`、本地方案的 `documentPath`、`inputPlanRoot`、`inputPlanRevision` 和 `schemeIndex`。按键类型为 `Button/Scalar/Vector2`，值与请求映射来自声明；按键不能由 UI 按角色名或技能名写死。方案 `rangeEndSeconds` 是日程内的相对秒数，SDK 起点由实际载入决定，不能据此伪造绝对 SDK 帧。

## 快速路线与快档画面

快速路线由 BTSMTL `QuickPreviewService` 执行，当前协议为 `btsmtl.native-timeline-preview/4`。项目适配捕获选定作者入口，并提供同一内容的语义 IR、声明默认值、子图、Timeline 内容与状态键。BTSMTL 建立静态查询、读取输入日程、推导路线并发布通用条件操作；项目适配把结果写成其正式运行包。存在播放器时，公共服务随后复用或启动隐藏的快档 Player、载入该包并定位，不经 Editor Native 预览作业。未注册播放器或目录缺包时，作业仍以 ready 交付计划，player/frame/adoptedMode 保持 null，notes 说明画面不可用；不会为纯路线写画面包。

| 方法与路径 | 输入与输出 |
| --- | --- |
| `POST /api/preview/quick/describe` | `PreviewQuickTarget{scenePath,root?,scenePublication?}`；无 root 时列出文档及入口，带 root 时返回本次捕获的来源、机器、输入方案与声明。 |
| `POST /api/preview/quick/plans` | `PreviewQuickRequest{mode:"quick",target,sourceRevision?,input,outputDirectory,rangeEndSeconds?,timeoutSeconds?,initialSeconds?}`；创建同一公共作业，推导并准备快档 Player。 |
| `GET /api/preview/quick/plans/{jobId}` | 读取作业、来源修订、计划、实际采用档位和播放器；可带 `stage` 等待阶段变化，最长 10 秒。 |
| `POST /api/preview/quick/plans/{jobId}/seek` | `{requestId,sourceRevision,inputRevision,seconds}`；返回同一作业的新采样和实际画面槽序号。 |
| `POST /api/preview/quick/plans/{jobId}/retime` | `{requestId,sourceRevision,inputRevision,timelineId,clips:[{clipId,startRaw,endRaw,clipInRaw?}],seconds}`；用完整时间候选在内存改时并返回更新后的 plan；有播放器时同时定位，不保存作者文档。 |
| `POST /api/preview/quick/plans/{jobId}/cancel` | 取消该作业并报告释放结果；请求已发不表示取消已完成。 |

`root` 分为 `nativeDocument`、`webEditingView`、`authoringDocument`，均携带 `authoring{documentPath,savedRevision,entry}`；网页来源另带同一正式文档的 `editingView`。入口是 `graph{graphId,machine}` 或 `timeline{timelineId}`。目录条目使用 `isDefault` 声明默认入口，网页不读取项目根字段来猜入口。场景选择以 publication.path＋revision 精确关联 quickScenes，quick 使用其 SceneAsset 身份，真实档继续使用原正式场景包。

`input.source` 为 `rootDefaults` 或 `documentInput{documentPath,savedRevision,inputId,schemeId}`，`input.revision` 与 `sourceRevision` 为 null 时按本次捕获的正式来源推导，非 null 时须匹配本次修订。输入 Timeline 的回调时刻、同刻冲突与非法节点由公共读取器处理；条件写入在 BTSMTL 处理，预览按键经项目的 `IPreviewInputKeyAdapter` 换成输入值和动作请求。API 的显式 `points/conditions` 覆盖须成对提供；网页只提交方案来源，不另造覆盖日程。

结果 `plan.sourceType=staticInference`，clock 使用秒和正式来源身份。`segments[].conditionDependencies` 回报该段实际推导所读取的条件，每项为 `{key,label,written}`；`written=true` 表示本次路线依赖输入方案中的条件写入，默认读取也保留并标为 false。未知谓词键使用所在图与节点的稳定身份；过期条件写入会报出输入 Timeline 的元素位置。

切到真实档时，网页保留同一来源和输入方案的快速路线，按正式程序绑定关联已缓存真实帧中的状态进入、退出事件，并并排显示条件依赖。该对比只使用当前帧缓存，明确显示已读范围；没有读到的历史不当作状态没有发生。内容、场景或输入方案变化后显示过期原因，不把静态计划写进真实运行记录。

作业 `player{liveId,processId,width,height}` 指明实际快档 Player。网页按 host.description.player 隐藏不可用的画面面板、真实运行及代码更新入口，并停止相关请求；纯路线仍可定位游标。像素只经桌面壳的 `quick` 共享画面区传递，网页按 `surfaceSequence` 读取槽并采用实际 `flipY`。普通浏览器只得到采样元数据。`adoptedMode` 只在运行端实际 Ready 后报告；success/pending、原 code/message/rawJson 以及 cleanupErrors 分别保留。成功、在途、失败分别使用 HTTP 200、202、409。部署与实际检查范围由项目接入记录持有。

## 作者文档与输入方案

作者内容以工作区文档为单位读写：`documentPath` 为工作区相对路径，`kind` 为文档种类（如技能、Timeline）。读写都带修订：编辑视图 `editingView` 为 `{writes:[{kind,documentPath,expectedRevision,files:{<相对路径>:<完整 JSON 原文>}}],reads:[{documentPath,revision}],execution?,scene?,product?}`，`writes` 是本次未保存内容，`reads` 是查询所依据的已读修订；任一修订与磁盘不符时拒绝，不覆盖外部修改。查询都可带同一 `editingView`，按未保存内容回答，不需要先保存。

| 方法与路径 | 参数或请求 | 响应 |
| --- | --- | --- |
| `GET /api/authoring/documents/types` | 无 | 文档类型身份、显示名、根类型、发现规则与语义角色 |
| `GET /api/authoring/documents` | `kind?` | 文档目录及适配提供的元数据 |
| `GET /api/authoring/documents/content` | `documentPath`，可选 `revision` | 文档原文与修订；带上已读 `revision` 且未变化时只回 `{success, changed:false, revision}`，不含正文 |
| `GET /api/authoring/documents/elements` | `kind?` | 文档种类的正式模板 |
| `POST /api/authoring/documents/editing-view` | `editingView` | 按编辑视图解析后的文档快照，不写盘 |
| `PUT /api/authoring/documents` | `editingView` | 只保存作者文档；修订冲突拒绝 |
| `POST /api/authoring/documents/apply?documentPath=` | `{editingView,preview?,job?}` | 保存并调用对应内容的编译，按需刷新 Live 或提交作业，见下文 |
| `POST /api/authoring/documents/document?documentPath=` | `{offset?,limit?,graphId?,editingView?}` | 文档的图与引用关系，不要求先创建运行 |
| `GET /api/authoring/documents/creation-options` | 无 | 创建类型、根类型、正式初始 configuration 与语义角色 |
| `POST /api/authoring/documents/create` | `{type,parameters:{configuration,entryGraph}}` | 未保存草稿，含 documentPath/rootFile/identity/name/files；名称取根合同的 displayName 字段 |
| `POST /api/authoring/documents/delete?documentPath=` | `{expectedRevision,editingView}` | 删除回执；仍有外部引用或共享内容时明确拒绝 |
| `POST /api/authoring/documents/exports/{kind}?documentPath=` | 对应导出请求 | `CompilationReport` 或导出产物回执 |
| `GET /api/authoring/documents/exports/files/{kind}/{key}/{path}?documentPath=` | 可选下载名 | 原导出文件 |
| `GET /api/authoring/publication/scopes` | 无 | 适配提供的发布范围目录，包含所需产品和载体 |
| `POST /api/authoring/references/replace` | `{action,domain,oldValue,newValue,context?,expectedRevision?}` | 文件差量、读修订及 affectedDocuments 完整文档路径 |
| `POST /api/preview/inputs/plans?documentPath=` | `{scene,sceneRevision,actorId,schemeName,rangeEndSeconds}` | 新方案、Timeline 与布局草稿，随后经通用文档保存 |
| `POST /api/preview/inputs/recordings?documentPath=` | `{archive,scene,actorId,planId?}` | 从已封存归档导入方案并返回内容、输入修订及文档回执 |
| `POST /api/preview/inputs/fragments` | `{scene,sceneRevision,actorId,key,operation,startSeconds,endSeconds}` | 正式轨道、片段、图和变量草稿 |
| `GET /api/project/folder` | `path?`，空值列正式根 | 文件夹、文件与适配提供的文件身份 |

目录与查询另有 `GET /api/authoring/catalog` 及 `POST /api/authoring/types/describe`、`/scopes/read`、`/resources/objects`、`/documents/queries/variables`、`/documents/variables/edit`，请求合同见 `Contracts/SkillAuthoringContracts.cs`。合同查询由 BTSMTL 挂载并交 `IAuthoringContractAdapter`，作用域与对象候选交 `IAuthoringResourceAdapter`。`GET /api/authoring/catalog` 的条目只含 `value/display/context/revision`，条目 `data` 由引用、范围与资源对象等单项查询给出；带上已读 `revision` 且未变化时 `changed:false`、`domains:null`。

3C 的 `/api/authoring/abilities/runtime/read` 与 `/configure` 由项目 Host 挂载，供 Unity 原生发布绑定读取、回写运行资产目标。

`inputs/fragments.operation` 仅为 `hold` 或 `press`。`hold` 使用给定起止秒数，`press` 的终点由宿主取起点加一个正式 tick。前端沿返回的身份、节点类型和完整片段写入，不另造按键位映射。

文档修订是文件内容的修订；技能文档的 `sourceRevision` 是正式作者内容闭包修订，两者职责不同。运行图是否可叠加还需对应 `programId/programRevision` 及实际调用。未保存内容不能继续显示旧运行值为当前结果。

文档 `relations` 从节点正式字段合同读取引用：`source` 给出原 owner、文件、graphId、nodeId、fieldId 与 serializedMemberPath；`target` 给出 `kind=graph|timeline`、id、displayName、owner、ownerName、file。图目标保留 role 与 ownership；Timeline 目标的这两项为 null。节点标题按钮、双击与检查器引用入口消费同一关系，先打开目标 owner，再按 kind 打开节点图或 Timeline；不从节点名称猜目标，也不要求先运行到该节点。

### 类型、字段、端口和模板

下表都为 `POST`，查询串带 `documentPath`，请求体可带 `editingView`。`offset` 默认 0 且不得为负，`limit` 默认 128、范围 1～128；分页结果还包含 `total/offset/limit/hasMore/items` 等查询字段。

| 路径 | 请求 | 核心数据 |
| --- | --- | --- |
| `/api/authoring/documents/queries/types` | 查询串 `search?`；体 `{offset?,limit?}` | 实际节点类型；`type,clrType,family,kind,nativeType,timelineDomains`，`clrType` 可以为 null |
| `/api/authoring/documents/queries/type` | `{type,configuration?,graphId?,offset?,limit?}` | 默认或当前草稿 `configuration`、字段分页及独立的端口可用性／分页 |
| `/api/authoring/documents/queries/nodes` | `{graphId?,offset?,limit?}` | 节点 |
| `/api/authoring/documents/queries/fields` | `{graphId,nodeId,offset?,limit?}` | 节点字段 |
| `/api/authoring/documents/queries/ports` | `{graphId,nodeId,offset?,limit?}` | 节点端口 |
| `/api/authoring/documents/queries/profile` | `{offset?,limit?}` | 文档档案字段 |
| `/api/authoring/documents/queries/references` | `{offset?,limit?}` | `identity,asset,referenceCount` |
| `/api/authoring/documents/queries/referrers` | `{identity?,target?,offset?,limit?}` | 指定身份的真实引用方 |
| `/api/authoring/documents/queries/reference-curves` | 与 referrers 相同 | 引用目标的曲线 |
| `/api/authoring/documents/queries/project` | 首页 `{offset?,limit?,editingView?}`；后续页 `{snapshot,offset,limit?}` | 文档所属工程条目；回执带 `snapshot`，后续页只带它、不再带 `editingView`，宿主从同一份快照切片。宿主只留最近 8 份快照，失效时报错，需从首页重读 |
| `/api/authoring/documents/queries/blackboards` | `{offset?,limit?}` | `identity,graphId,id,key,type,category` |
| `/api/authoring/documents/queries/elements` | 查询串 `timelineId?` | 图锚点、状态转移、轨道、片段、标记、输入节点和黑板的正式模板及存储形状；图模板带 `role/seed/ownership`，`value` 与已读入图同为展开模型；`state` 为状态机新状态模板，`flowOutputs` 列出带执行出口列表的节点成员及其元素形状 |

场景方案使用同一组文档查询，documentPath 为其 Scenarios 相对路径；类型和元素由输入方案来源读取，不要求能力根。

新节点先按实际 `type` 请求默认配置；编辑后将当前未保存配置传回同一个 `queries/type`，不能为了查询字段而先保存。字段包含正式 `serializedMemberPath`、存储 `shape`、`readOnly`、`optional`、`allowedValues`、`pickerKind/pickerOwnerField/objectType`、`visibleWhenField/visibleWhenValue`、`choices` 和 `references`；计算字段为 `computed:true`，不写回。存储形状成员带 `authoring`（`fieldId,displayName,authoringVisible,readOnly,allowedValues,optional,graphTargets`），`graphTargets` 非空的成员是专属图槽位（目标 `role/ownership/ownerBinding/seed`），检查器按槽位提供打开、新建与可选时的移除，其余只显示作者可见的成员；组合节点步骤的中断策略可选值随所属组合节点给出。

`elements` 提供 `anchors/transition/tracks/clips/marker/previewInput/blackboard`；轨道和片段模板带 `kind/clrType/value/shape/structureMembers`，`structureMembers` 是由时间轴结构编辑、不作为普通字段的成员。输入节点类型、按键成员路径和各值类型成员从 `previewInput` 取。

整体 `availability.available=true` 不等于端口可用。例如宏输入节点缺少所属 Subgraph 上下文时，HTTP 仍为 200，`fields` 可读，但 `ports` 为 `{availability:{available:false,reason},errors:[...]}`，没有 `hasMore/items`。端口可用时才是 `{availability:{available:true,reason:null},hasMore,items,...}`。UI 保留该原因并在上下文补齐后重查，不能把它显示成“此节点没有端口”。

### 保存、编译与采用

`documents/apply` 的请求为 `{editingView,preview?,job?}`：`editingView.writes` 只需包含本次修改的文档，每份带读取时的 `expectedRevision`；可选 `preview` 为 `{requestId,liveId,projectParameters,channels}`；`projectParameters` 由适配解释；可选 `job` 是下文完整 `StartPlayerJobRequest`。

| 保存结果 | 必须处理的回执 |
| --- | --- |
| 技能内容成功 | HTTP 200，`saved:true,compilationState:"succeeded",content,compilation,preview,jobError`；`compilation` 含 `compilationKey,sourceRevision,artifactReceiptPath,reused` |
| 仅输入闭包修改／场景方案 | HTTP 200，`saved:true,compilationState:"not-required",content,inputs,preview,job`；没有技能 `compilation` |
| 作者格式或编译拒绝 | HTTP 422，`saved:true,content,stage,compilationState`，附 `diagnostic` 或原 `error/output/exitCode`；文件已经落盘，不能展示为“没有保存” |
| 修订冲突 | HTTP 409；没有写盘，需重读后再编辑 |

内容刷新回执单独使用 `stage=accepted/adopted/failed/unavailable`，并提供 `statusPath`；通过 `GET /api/preview/refreshes/{id}` 继续读取原请求。只有实际载入 ACK 为 applied 才为 adopted。只改输入时，宿主更新引用本目录方案的实际输入修订后载入或提交作业；包含技能内容的保存不能直接以旧场景包自动启动新档案，回执会给出 `jobError`。

作者库写入由跨进程文件锁串行（工作区根 `.transaction.lock`），Unity Editor 与 Host 同时保存时后到者等待，持锁超过 30 秒报错；引用、私有图与 Timeline 的新增删除应放在同一次 `editingView` 中提交。

## Live 生命周期、载入与控制

| 方法与路径 | 请求／参数 | 响应或用途 |
| --- | --- | --- |
| `GET /api/preview/live` | 无 | `LivePlayer[]` |
| `POST /api/preview/live` | `{requestId,playerBuildId,width,height}` | 实际 Live；启动进程不代表 hello、内容载入或画面就绪 |
| `GET /api/preview/live/{id}` | 无 | `LivePlayer`，含 `identity,previewTarget,latestSample` |
| `POST /api/preview/live/{id}/load` | `{commandId,content,channels,executionMode:"interactive",quality?}` | 命令阶段和真实 ACK；quality 默认 75 |
| `POST /api/preview/live/{id}/commands` | 下表命令 | `stage,commandId,ack` |
| `GET /api/preview/live/{id}/commands/{commandId}` | 无 | 原命令回执；不因等待超时生成另一条命令 |
| `POST /api/preview/live/{id}/stop` | `{}` | 结束进程后的 Live；区别于内容控制命令 stop |
| `POST /api/preview/live/{id}/swap` | `{playerBuildId}` | 使用正式新版本替换该 Live，返回实际状态 |
| `POST /api/preview/players/code-updates` | `{requestId,playerBuildId}` | `state=created/unchanged/rejected`、新 `buildId`、父版本及拒绝原因；创建版本不等于已 swap |
| `GET /api/preview/live/{id}/captures/{name}` | capture ACK 返回的名称 | 明确请求产生的 PNG |
| `WS /api/preview/live/{id}/view` | 同源连接 | 文本状态／回执与二进制画面；可发送正式控制命令 |

Load 的 `content` 是二选一：

```json
{"kind":"scene","scene":"<来源 path>","revision":"<场景 revision>","source":{"kind":"local","lastSdkFrame":240,"players":[{"actorId":"<实际角色>","inputPlanRoot":"<实际方案路径>","inputPlanRevision":"<输入修订>","schemeIndex":0}]}}
```

```json
{"kind":"timeline","package":"<来源 content.package>","revision":"<来源 content.revision>","timeline":"<来源 content.timeline>"}
```

场景 source 还支持 `{kind:"replay",path,revision}`、`{kind:"recording",runId,revision}` 和交互设备 `{kind:"device",lastSdkFrame}`。使用目录实际返回的来源和修订，不能凭显示名拼路径。批量档案请求与 Load 的完整 C# DTO 见 [PreviewRunRequest.cs](../../../3C/Tools/CharacterWorkbench/Client/PreviewRunRequest.cs)；场景 Load 的录制参数属于正式作业链，普通交互界面直接复用已有客户端类型。

`LivePlayer.identity` 提供真实 `liveProtocol/observationProtocol/executionModes/channels/imageEncoding/running`。未收到 hello 时协议可以为 null，不能由构建名称推断支持。`previewTarget` 来自实际 Load 采用，包含 `loadCommandId/playerBuildId/content/channels`；本地按钮状态不能替代它。

| 命令 `type` | 必需载荷及语义 |
| --- | --- |
| `play/pause/stop/capture` | `commandId` |
| `step` | `commandId,count` |
| `seek` | `commandId,position:{hostFrame:"目标键"}`；手工数字和 Host 提供的目标均走此入口，由原 Host 按实际采用内容解析为运行端位置 |
| `pick` | `commandId,x,y`；命中结果按 `PlayerPick` 回执读取 |
| `camera` | `mode:"orbit"或"game"`，可带 `focusActorId,reset,yaw,pitch,zoom,panX,panY`，沿现有相机控制合同发送 |
| `input` | 完整 `InputCommand`：`keys,mouseDx,mouseDy,mouseButtons,scroll,x,y,lx,ly,rx,ry,lt,rt,padButtons` |
| `stream` | `enabled`，可选 `quality,maxFps` |
| `surface` | `commandId,surfaceHandle,surfaceBytes,width,height`；桌面壳建好共享画面区并把句柄复制进播放器进程后由网页发送，句柄 0 撤下 |

命令阶段为 `sent/submitted/applied/rejected/not-applied`。HTTP 成功及 submitted 只表明已提交；UI 使用实际 ACK 的 `ok/error/content/clock/playerBuildId` 确认结果。状态还区分 Player 的 `idle/loading/paused/playing/stepping/seeking/failed`，不能与进程 `starting/running/exited` 混为一个枚举。

### 时钟与画面身份

实时采样与档案使用同一公共 `PreviewFrameSource`，完整字段见下文公共合同表；执行身份取 `execution`，载入和画面分别保留 `loadCommandId/sequence`，内容保留正式 `content` 及绑定修订。实际位置、秒数和显示名取 `clock.position.hostFrame/seconds/positionDisplayName`；请求位置独立保存在 `clock.requestedPosition`。网页不解读运行端时钟种类或换算帧率。

`Live/status.player.transport` 为可空 `PreviewTransportAction[]`，每项 `{action,available,reason,target}`，`target` 可为 null 或 `{hostFrame}`。旧运行端未提供资格时，Host 保留实际状态和来源，返回 `transport=null` 与明确 `transportUnavailableReason`，不能补造数组或阻断其它实例目录；网页显示原原因并禁用控制。数组存在时，`seek` 描述任意位置定位资格，`first/previous/next/last` 的目标由 Host 按原规则提供，`play/pause/step` 同样使用实际状态的资格。网页不从时钟种类推断；上一帧、首末帧只使用正式目标，下一帧仍发原 `step,count:1`。纯 Timeline 不制造角色或 Quantum CRC。

画面 WS 文本消息为 `status/frames/ack/pick/error/extension`；状态和专用扩展保留 `rawJson`。二进制消息格式为 `[4字节 little-endian 元数据长度][UTF-8 JSON]`，元数据含 `hostFrame,source,width,height,flipY,worldToClip`，不带像素。像素在桌面壳（`BTSMTL.WebWorkbench.Shell`）的共享画面区：64 字节头（宽、高、帧槽数、槽头长）加 3 个帧槽，每槽 256 字节槽头（槽序号、采集与写完时刻）加宽×高×4 RGBA；网页按 `source.sequence` 找槽，读完再核对槽序号未被覆盖。普通浏览器没有共享画面区，不显示实时画面；按请求的截图用 `capture`。直接复用 `openViewer` 与 `shell/sharedSurface.ts`，不能在浏览器消费 Native Player 的内部二进制头。

HTTP Live 与 WS `status.live` 共用同一完整读取器，均解码 `player.source/transport`、`latestSample` 与实际内容／绑定身份；Live 顶层没有 `rawJson`，原状态文本位于 `player.rawJson`。HTTP 和 WS 的 ACK 也共用读取器，保留 Host 的 `clock/content/source/hostFrame/rawJson`，允许正式空值，不在客户端重建帧号或原始记录。文本与视频均在原 `openViewer` 边界解码；协议字段缺失会清除当前时钟、目标与画面并进入已有错误记录，不继续显示旧样本为当前成功结果。

新 Load 后清除旧画面；接收图像时核对该图自身的 Load、Player build、内容／绑定修订及对应时钟。图片角标显示图片自身采样，不借另一次状态消息的帧号。只有同一 WS 帧消息的 source 与共享画面区里同 `sequence` 且读后未被覆盖的帧槽能组成同次画面。

自动载入按 `LivePlayer.previewTarget.content` 判断是否已经采用所选场景。回跳重建期间 `player.source` 可以暂时为空，场景仍属于原 Load；图像接收仍核对实际采样身份。

状态、画面和命令通过同一来源比较：执行身份、构建／Editor程序集、Load、内容、文档／绑定及场景／输入修订均参与；实际时钟和采样序号各自保留，不把连续采样误作新 Load。来源改变、画面连接关闭或新 Load 开始时清除旧时钟、定位目标、回执和画面；旧来源状态及迟到回执不得关联新目标。既有 HTTP 列表刷新只在 WS 尚未打开时供同一选中执行初始化状态，不增轮询或第二次取图。最后命令保留完整原回执；submitted 后到达的同 commandId WS ACK 更新同一条记录，仍需核对实际内容与来源。

## 正式作业与档案

| 方法与路径 | 请求／用途 | 响应合同 |
| --- | --- | --- |
| `POST /api/preview/player-jobs/creation-options` | `{requestId,projectParameters,width,height}`；先取得项目正式默认范围及输出目录 | `requestId,projectParameters,outputDirectory,width,height,quality,channels,mode,frameRangeBasis` |
| `POST /api/preview/player-jobs` | 完整 `StartPlayerJobRequest` | `PlayerJobReceipt`，包含原请求、job/build 身份、stage 和 `statusPath` |
| `GET /api/preview/player-jobs` | 作业列表与队列情况 | `capacity,concurrency,queued,archiveCacheCapacity,items` |
| `GET /api/preview/player-jobs/{id}` | 当前作业及可用档案 | `{receipt,archive}` |
| `POST /api/preview/player-jobs/{id}/cancel` | `{}` | `{jobId,cancellationRequested,jobStage,reason}`；发出取消不等于已结束 |
| `GET /api/preview/player-jobs/{id}/archive` | 档案摘要（`IPreviewArchive.Archive`） | `PreviewArchiveDigest`：`jobId,inputMode,frameCount,rawJson`；网页只读取前三项，`rawJson` 为项目原清单 |
| `GET /api/preview/player-jobs/{id}/files/{path}` | 清单内的原文件路径 | 原文件正文 |
| `GET /api/preview/player-jobs/{id}/summary` | 档案概况 | `PlayerSummary`，含实际帧范围、场景／输入／内容修订及通道可用性 |

作业请求包含 `requestId,playerBuildId,projectParameters,outputDirectory,width,height,quality,channels,mode`；新作业 `mode` 为 `real`。`projectParameters` 是适配接收并产出的不透明 JSON，网页把 creation-options 返回的值原样送回；3C 在其中提供场景、输入来源、帧段与截图帧，公共服务不解释这些字段。输出目录使用返回的 D 盘路径。请求、Live、构建和档案身份分别保存。

作业 stage 为 `queued/running/completed/failed/cancelled/rejected/unavailable`。按原 `statusPath` 读取，直到实际终态；失败是否有可读档案取原清单／回执，不用 stage 名字捏造空档案。

档案下表全部为 GET。范围参数为 `firstFrame,lastFrame,offset?,limit?`；范围使用 Host 的原帧键，不是网页列表索引。网页原样提交十进制文本，由原 Host 绑定到其实际帧范围；不转换为 SDK 帧或另建范围协议。分页 `limit` 默认 128。各通道结构直接使用 playerRecords 中对应读取器；不得假设所有结果都有同一分页外壳。

| 完整路径 | 参数／返回用途 |
| --- | --- |
| `/api/preview/player-jobs/{id}/frames` | `offset?,limit?`；`Page<HostFrame>` |
| `/api/preview/player-jobs/{id}/observations` | 范围参数；含帧、sections、路线、几何、原始值及 availability 的统一面板读取 |
| `/api/preview/player-jobs/{id}/geometry` | 静态世界、角色形状和几何布局 |
| `/api/preview/player-jobs/{id}/source-map` | `offset?,limit?`；原来源记录 |
| `/api/preview/player-jobs/{id}/programs` | `offset?,limit?`；实际程序与修订 |
| `/api/preview/player-jobs/{id}/events` | `offset?,limit?,actorId?`；原事件 |
| `/api/preview/player-jobs/{id}/route/frames` | 范围参数；图／状态／Timeline 路线、节点发生、边与端口 |
| `/api/preview/player-jobs/{id}/ports` | 范围参数；原端口采样 |
| `/api/preview/player-jobs/{id}/physics` | 范围参数；原物理记录 |
| `/api/preview/player-jobs/{id}/logic` | 范围参数；逻辑身体、FP 原值和 CRC |
| `/api/preview/player-jobs/{id}/pose-layout` | 骨架、父子关系、角色 roster、坐标与单位 |
| `/api/preview/player-jobs/{id}/presentation` | 范围参数，可加 `rosterIndex?`；实际表现姿态 |
| `/api/preview/player-jobs/{id}/camera` | 范围参数；实际相机 |
| `/api/preview/player-jobs/{id}/input` | 范围参数；实际采用输入及日程修订 |
| `/api/preview/player-jobs/{id}/unavailable` | 范围参数；正式缺失原因 |
| `/api/preview/player-jobs/{id}/curves` | `offset?,limit?`；可读曲线目录 |
| `/api/preview/player-jobs/{id}/curves/samples` | `curveId` 与范围参数；所选曲线原采样 |
| `/api/preview/player-jobs/{id}/frame-images` | 范围参数；实际图像索引、原 SDK 帧及表现采样身份 |
| `/api/preview/player-jobs/{id}/frame-images/{imageIndex}/metadata` | 指定 JPEG 的元数据 |
| `/api/preview/player-jobs/{id}/frame-images/{imageIndex}` | 指定实际图像的字节；`imageIndex` 不等于 SDK 帧 |
| `/api/preview/player-jobs/{id}/images/{sdkFrame}/metadata` | 明确捕获的 PNG 元数据 |
| `/api/preview/player-jobs/{id}/images/{sdkFrame}` | 明确捕获的 PNG 字节；没有捕获则保留不可用结果 |

观察通道为 `logic,bones,camera,physics,graph,stateMachine,timeline,blackboard,ports,input,images`。`available=false`、`complete=false` 和空数组含义不同；按原 `reason` 展示，不能以静态默认值或其它帧补齐。检查器同时消费摘要与当前帧的 availability，同一缺项原因只列一次。通用 sections 是宿主提供的显示名、字段和来源定位，前端无需为每种内部运行值新增硬编码页签。

画面通道能力取同一 Summary。明确捕获的图片沿原 `/images/{宿主帧键}/metadata` 查询实际可用性和原因，不解析项目清单的 captures 或 SDK 成员来猜画面是否存在；仅 metadata.available 为 true 时再取字节。Editor 的原画面构建号可以为 null，真实执行身份仍来自 Summary／Source。

封存连播中同时读取一张 JPEG。浏览器保留上一张实际画面及其来源帧，读取完成后再跟进最新游标。停播和手动定位以所选记录为准，档案缺图时显示该帧的实际缺失原因。切换或退出档案时结束图像请求并释放缓存。

JPEG 的来源 SDK 帧与当前分页帧一致时，叠加使用分页中的姿态和相机，并按实际表现序号及相机实例关联。来源 SDK 帧与分页帧不同时，读取 JPEG 指定的来源帧。Gizmos 按已绘制的采样投影。封存回看期间暂停构建与实时会话列表的定时刷新，退出回看后恢复。

档案游标使用通用帧的 `hostFrame/seconds`，总时长使用实际首末帧秒数之差；通过既有 `/frames` 按需读取首末各一条，复用同一时间基准，不从项目清单反算。通用帧的 `source` 可按正式合同为 null，几何接触 `kind` 使用宿主给出的文本分类。截图就绪标记的帧号与可选一致性值也直接取当前通用帧；缺少项目逻辑通道不阻止已完成绘制的面板报告就绪。

档案来源和摘要使用 [PreviewObservationContracts.cs](Contracts/PreviewObservationContracts.cs) 的公共合同，JSON 字段采用 camelCase：

| 公共类型 | 字段与消费要求 |
| --- | --- |
| `PreviewExecutionIdentity` | `kind?,identity?,playerBuildId?,editorAssemblyMvid?`；四项均明确可为 null。旧档案未记录的执行类型／身份显示不可用，原因由 Host 的 availability 提供；不得以作业号、构建号或默认 kind 推导。保留现代记录的真实 Player／Editor 身份，不为 Editor 制造构建 ID。 |
| `PreviewPosition` | `hostFrame`；原 Host 查询／定位 key。命令与采样使用同一形状，不包含原生时钟 kind 或另一套 request。路线与输入缓存按正式记录序号排序，历史路线按实际 seconds 截止，不用查询 key 做整数运算。 |
| `PreviewTransportAction` | `action,available,reason,target?`；target 明确可为 null，资格和前后／首末目标由原 Host 提供。 |
| `PreviewClock` | `position,seconds,positionDisplayName,requestedPosition?,fields`；请求位置与实际位置分开，显示字段直接取 Host，seconds 使用所属真实时间轴。 |
| `PreviewContentIdentity` | `kind,revision,documentId?,contentIdentity?,contentRevision?,bindingRevision?`；除前两项外均可为 null，实际内容和绑定修订随来源保留。 |
| `PreviewFrameSource` | `execution,contentRevision,sceneRevision?,inputSource?,inputRevision?,clock,loadCommandId?,sequence?,content?`；问号项明确可为 null。独立 Timeline 没有场景修订或 Quantum 输入来源时保留 null，不猜方案或场景身份；未记录不补身份。 |
| `PreviewSummary` | 原摘要字段保留，原 `playerBuildId` 改为必填 `execution`；构建 ID 从 `execution.playerBuildId` 读取。 |

曲线按 `source.clock.seconds` 排序和定位；嵌入作者 Timeline 时，按当前通用帧的真实秒数与作者游标对齐，不再用 SDK 帧差和 tickRate 换算。采样帧号和名称取 `source.clock.position.hostFrame`、`positionDisplayName`，项目时钟附加信息显示 `clock.fields`。档案上下文保留原 Summary，范围和帧数只从它读取；档案网格／吸附使用 Summary 的可空 `frameStepSeconds`，不从项目清单或平均 FPS 推算。

`/input` 每条记录的 `inputTimeSeconds` 为正式方案时间，开始前为 null；页级 `inputTimeOriginSeconds` 是输入零点在档案中的实际秒数，`inputTimeStepSeconds` 是原时钟提供的样本步长。旧缺实际生效边界时三项为 null，原因取页级 `inputTimeUnavailableReason`。输入观察仍须匹配角色、方案路径、修订和 scheme；时间不能代替关联。定位沿既有 `seekTime(origin+方案秒)` 查实际帧记录，最后只发送原宿主帧键；显示和输入持续区间只用正式时间／步长，连续区间按原缓存记录序号判定，不做 SDK 算术。方案开始前不制造零秒样本，但已知真实零点时仍可定位后续方案位置。

上述公共来源、实时控制与通道范围尚未完成运行采用检查；已部署的旧形状不会在前端兼容解码。

图与 Timeline 运行叠加使用完整 `ExecutionCall`：`actorId,programId,programRevision,invocationPath,parentInvocationPath,actionId,executionGeneration,invocationGeneration,parentInvocationGeneration,playbackId`。Timeline 还区分 Logic／Presentation 域；多个实际调用需要明确选择，固定后缺失当前记录不借另一实例。`loopIteration` 为可空的真实记录，不按事件出现次数估算。作者图实例路径与实际运行路径分别保留，不能裁后缀匹配父调用。

## 动画资源与宿主扩展

| 方法与路径 | 参数／请求 | 响应 |
| --- | --- | --- |
| `GET /api/resources/animations` | 无；Host 需配置 AnimationLibrary | `AnimationLibrary` 正式清单与内容 SHA256 |
| `GET /api/resources/animations/file/{hash}` | 清单实际哈希；先读取清单 | glb，支持 Range 和 ETag |

宿主扩展的 HTTP 接口与 Live 命令由宿主自行登记和记录，不进入本表。网页扩展在宿主构建时经 `BTSMTL_WORKBENCH_EXTENSIONS` 组合，入口模块默认导出 `WorkbenchExtension`（停靠面板、画面工具），只经 `@btsmtl/workbench`（[src/sdk.ts](web/src/sdk.ts)）使用工作台。扩展命令走同一个 Live `/commands`（`playerService.hostCommand`），回执附加字段原样保留给扩展解析；Player 状态附加字段在 `rawJson` 中。

`WS /api/preview/live/{id}/player?token=...` 是 Native Player 的回连入口，供宿主启动的 Player 使用，UI 使用 `/view`，不持有或转发该 token。

## 错误、取消与当前能力边界

公共异常响应为 `{error}`：格式或参数错误 HTTP 400，找不到文件／身份 404，状态或修订冲突 409，其它宿主异常 500。作者严格读取／编译可返回前述 422，含 `code/message/errors` 或保存分支回执。动画资源缺失为 409、清单无效为 422、未知资源版本为 404；保留其具体 `code/message`。

字段／端口部分不可用可能仍是 HTTP 200；运行、刷新和作业也有各自业务拒绝阶段。UI 不能只看 HTTP 2xx。客户端 [http.ts](web/src/api/http.ts) 保留 `ApiError.status/reason/raw/path`；切换文档和查询上下文时取消旧只读请求，原命令结果未确认则按原 commandId 重查。

目前没有单独的技能目录复制／重命名／删除协议，也没有独立 Timeline 作者目录的 CRUD 协议。技能私有 Timeline 随技能 JSON 保存；已发布独立 Timeline 的目录、Load、时钟和画面由上文接口提供。场景方案只开放表中三种 `queries`，没有技能专属 `document/references/blackboards` 路由。UI 有新增业务操作时，应先补对应正式接口及类型，不在本地硬编码另一套作者规则。

当前项目预览使用 Player 作业、档案 HTTP 读取和 Live WebSocket；没有供本前端调用的旧 `/api/preview/runs/*` 控制或 SSE 流。运行控制不由网页自行解释技能节点实现。

## 通用网页依赖检查

构建守护的 `BTSMTL_WORKBENCH_REGISTERED_ROUTE` 从工作台 C# 的 `Map*` 与 `MapGroup` 收集正式路径，检查网页 `/api/` 字面量和模板前缀；`BTSMTL_WORKBENCH_DECLARED_FIELD` 从两个核心包的声明收集 `m_` 成员，再检查去除注释后的 TS/TSX 语法节点。项目扩展目录不在通用网页扫描范围，通用源码不能用专有接口或字段绕开适配。解析器、输入身份与退出规则见 [DependencyGuard](../DependencyGuard/README.md)。
