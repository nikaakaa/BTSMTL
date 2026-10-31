# Web Workbench 前端接口

本文面向 UI worktree 的开发者，按实际界面操作说明现有 HTTP、WebSocket 和数据合同。通用网页层提供资源与画面适配，3C `CharacterWorkbench.Host` 提供技能作者、场景输入、正式 Player 与档案服务。UI 负责布局、样式、手势和本地编辑事务；宿主负责正式作者数据、类型语义、编译、运行及实际结果。

本文对应当前源码合同。正在运行的 Host、Player 和静态前端必须采用相应版本；接口说明和源码存在不表示当前端口已采用。部署与实际检查结果见 [3C 作者接口记录](../../../3C/Tools/CharacterWorkbench/Host/Authoring/README.md)。

## 接入与公共规则

- HTTP 使用相对路径 `/api/...`，JSON 请求使用 `Content-Type: application/json`，文本使用 UTF-8。HTTP 外层属性名为 camelCase，作者原文及节点成员保持正式存储命名（如 `AuthoringId`、`ID`、`m_...`）。本文路径参数用 `{name}` 表示，实际拼入时编码。
- WebSocket 使用同源的 `ws://` 或 `wss://`。Vite 当前把 `/api`（含 WebSocket）代理到 `http://127.0.0.1:5367`；联调其它端口时，UI 开发配置应指向该实际 Host。
- 独立启动通用网页服务只装配静态页面、动画资源及不可用的 `/api/preview/host`；完整编辑与运行使用项目 Host。技能和场景作者接口需 Host 启用 `SkillAuthoring`。
- 作者文件的 `content` 是完整 JSON 原文字符串。读取、编辑和写出沿用 [skillJson.ts](web/src/state/skillJson.ts)，保留大整数及 `1.0` 等正式浮点写法；不能经过普通 `JSON.parse`／`JSON.stringify` 整体往返。
- 公共帧键、64 位身份、序号、FP Raw 和修订保持字符串。定位请求只提交 `position:{hostFrame:"目标键"}`，由原 Host 按实际采用内容解释；网页不转换为 SDK 帧或 Timeline tick。
- 查询分页有两种原合同：作者查询用 `hasMore`，档案分页用 `nextOffset`（终点为 `null`）。按各自回执翻页，不把二者混用。
- 三维公共几何采用右手坐标、Y 轴向上、米；四元数顺序为 `[x,y,z,w]`。画面使用实际 `flipY` 和列主序 `worldToClip`。

字段的可调用 TypeScript 入口在 [api/authoring.ts](web/src/api/authoring.ts)、[api/preview.ts](web/src/api/preview.ts)、[api/player.ts](web/src/api/player.ts)、[api/resources.ts](web/src/api/resources.ts)。运行数据的解析器及导出类型在 [api/playerRecords.ts](web/src/api/playerRecords.ts)，图、调用、画面和资源类型在 [api/types.ts](web/src/api/types.ts)。这些文件供 UI 直接复用，不另写一套同名 DTO 或协议解析器。

## 界面操作与接口分工

| 界面操作 | 宿主提供的输入或处理 | UI 处理及输出 |
| --- | --- | --- |
| 打开技能、FSM、子图或私有 Timeline | 文件快照、`document` 展开结果、图及元素稳定身份 | 维护当前文档、图实例路径和选择，渲染画布 |
| 添加节点、状态、转移、轨道或片段 | `queries/types`、`queries/type`、`queries/elements` 的正式类型、默认值、字段和端口 | 使用返回模板构造本地草稿；新增节点和状态自动选中交给检查器，不复制 C# 默认值或维护节点白名单 |
| Inspector 改字段、动态端口或引用 | 按当前未保存 `configuration` 描述字段、条件显示和端口；引用与黑板查询 | 显示对应控件，将结果写回原作者成员路径，并重查受影响的端口 |
| 拖节点、拉片段、框选、连线、复制删除、撤销重做 | 正式身份、作者文件与保存入口 | 手势及本地事务；节点位置和内容改回对应 JSON，统一保存。流图连线换接端点保留原连线 UID，换源时迁到新源节点的 outConnections，沿原事务撤销。每次鼠标移动无需 HTTP 请求 |
| 新建场景输入方案、添加按住或单按输入 | 场景角色及按键声明、`draft`、`input-fragments` | 选择实际角色和按键，编辑返回的方案／轨道／片段／图，保存整个变更闭包 |
| 保存并采用 | 文件写入、编译状态、输入修订、Player 刷新或作业回执 | 分别显示保存、编译、采用和作业阶段，按原状态地址继续读取 |
| 播放、暂停、逐帧、跳转、相机和试玩输入 | Live、内容载入、命令及真实 ACK、画面 WS | 管理控件状态；按实际内容时钟显示，按采样身份接收画面 |
| 回看结果、检查调用、端口与曲线 | 同一作业的分页档案、完整实际调用身份、可用性和原始值 | 按同一来源关联面板，保留缺失或不完整原因 |
| 动画资源及专用调参 | 动画清单／版本文件；已登记扩展的读写与命令合同 | 按元数据展示，不在 UI 实现运行算法 |

## 来源、能力与输入声明

| 方法与路径 | 参数 | 响应及用途 |
| --- | --- | --- |
| `GET /api/preview/host` | 无 | `{available,description}`；description 提供 `filters`、`inputLimit` 和 `modes`，各模式含 `available/reason/createsRun/defaultChannels` |
| `GET /api/preview/sources` | 无 | `Sources`：`scenes[{path,revision}]`、`timelines`、`replays` 及正式目录 |
| `GET /api/preview/inputs` | `scene,revision` | `InputCatalog`：`revision,tickRate,actorTargets,localPlanRangeBasis,players`；各角色含 `localPlans,keys,values,requests` |
| `GET /api/preview/images` | 无 | `{available,description}`；图片适配能力 |
| `GET /api/preview/players` | 无 | `{items:PlayerBuild[]}`；`buildId,displayName,fields,rawJson` |

`scenes` 的 `path` 与 `revision` 成对传递。`timelines` 每项含可直接用于 Load 的 `content`，以及 `contentIdentity/contentRevision/bindingRevision/tickRate/scene/bindings`；不能把它改装成场景输入来源。

输入选项使用宿主实际返回的 `actorId`、`inputPlanRoot`、`inputPlanRevision` 和 `schemeIndex`。按键类型为 `Button/Scalar/Vector2`，值与请求映射来自声明；按键不能由 UI 按角色名或技能名写死。方案 `rangeEndSeconds` 是日程内的相对秒数，SDK 起点由实际载入决定，不能据此伪造绝对 SDK 帧。

## 技能与场景作者文件

| 方法与路径 | 参数或请求 | 响应 |
| --- | --- | --- |
| `GET /api/authoring/skills` | 无 | 技能目录名 `string[]` |
| `GET /api/authoring/skills/{skill}` | 无 | `SkillSnapshot`：`{skill,revision,files:[{path,content}]}` |
| `GET /api/authoring/skills/{skill}/changes` | `revision` | `{skill,revision,changed,files:[{path,revision}]}` |
| `GET /api/authoring/skills/{skill}/document` | 无 | `{skill,programId,sourceRevision,tickRate,graphs:GraphDefinition[]}`；不要求先创建运行 |
| `PUT /api/authoring/skills/{skill}` | `SkillSaveRequest`，见下文 | 编译保存或输入保存的分支回执；拒绝时保留正式诊断 |
| `GET /api/authoring/scenarios` | 无 | 场景方案目录名 `string[]` |
| `GET /api/authoring/scenarios/{scenario}` | 无 | 与 `SkillSnapshot` 相同文件快照结构 |
| `GET /api/authoring/scenarios/{scenario}/changes` | `revision` | 与技能 changes 相同结构 |
| `PUT /api/authoring/scenarios/{scenario}` | `SkillSaveRequest` | 输入保存回执，`compilationState="not-required"` |
| `POST /api/authoring/scenarios/{scenario}/draft` | `{scene,sceneRevision,actorId,schemeName,rangeEndSeconds}` | `{files:[{path,content}]}`；只生成草稿，尚未写目录，随后用 PUT 保存 |
| `POST /api/authoring/scenarios/{scenario}/recordings` | `{archive,scene,actorId,planId?}` | `{content,input:{planId,path,revision}}`；实际写入／更新录制方案，`archive` 是归档 `archive.json` 路径 |
| `POST /api/authoring/input-fragments` | `{scene,sceneRevision,actorId,key,operation,startSeconds,endSeconds}` | `{track,clip,graph,tickRate,sceneRevision,availability}`；返回正式 JSON 对象，尚未保存 |

`input-fragments.operation` 仅为 `hold` 或 `press`。`hold` 使用给定起止秒数，`press` 的终点由宿主取起点加一个正式 tick。前端沿返回的身份、节点类型和完整片段写入，不另造按键位映射。

文件快照 `revision` 是文件内容哈希清单的修订；`document.sourceRevision` 是正式作者内容闭包修订，两者职责不同。运行图是否可叠加还需对应 `programId/programRevision` 及实际调用。未保存内容不能继续显示旧运行值为当前结果。

### 类型、字段、端口和模板

下表技能查询前缀为 `/api/authoring/skills/{skill}/queries`。`offset` 默认 0 且不得为负，`limit` 默认 128、范围 1～128；分页结果还包含 `total/offset/limit/hasMore/items` 等查询字段。

| 方法与完整路径 | 参数或请求 | 核心数据 |
| --- | --- | --- |
| `GET /api/authoring/skills/{skill}/queries/types` | `search?,offset?,limit?` | 实际节点类型；`type,clrType,family,kind,nativeType,timelineDomains`，`clrType` 可以为 null |
| `POST /api/authoring/skills/{skill}/queries/type` | `{type,configuration?,graphId?,offset?,limit?}` | 默认或当前草稿 `configuration`、字段分页及独立的端口可用性／分页 |
| `GET /api/authoring/skills/{skill}/queries/nodes` | `graphId?,offset?,limit?` | 已保存节点 |
| `GET /api/authoring/skills/{skill}/queries/fields` | `graphId,nodeId,offset?,limit?` | 已保存节点字段 |
| `GET /api/authoring/skills/{skill}/queries/ports` | `graphId,nodeId,offset?,limit?` | 已保存节点端口 |
| `GET /api/authoring/skills/{skill}/queries/references` | `offset?,limit?` | `identity,asset,referenceCount` |
| `GET /api/authoring/skills/{skill}/queries/referrers` | `identity,offset?,limit?` | 指定身份的真实引用方 |
| `GET /api/authoring/skills/{skill}/queries/blackboards` | `offset?,limit?` | `identity,graphId,id,key,type,category` |
| `GET /api/authoring/skills/{skill}/queries/elements` | 无 | 图锚点、状态转移、轨道、片段、标记、输入节点和黑板的正式模板及存储形状 |
| `GET /api/authoring/scenarios/{scenario}/queries/types` | `search?,offset?,limit?` | 场景方案的类型查询 |
| `POST /api/authoring/scenarios/{scenario}/queries/type` | 与技能 type 请求相同 | 场景方案的默认／草稿配置描述 |
| `GET /api/authoring/scenarios/{scenario}/queries/elements` | 无 | 场景输入编辑模板 |

新节点先按实际 `type` 请求默认配置；编辑后将当前未保存配置传回同一个 `queries/type`，不能为了查询字段而先保存。字段包含正式 `serializedMemberPath`、存储 `shape`、`readOnly`、`optional`、`allowedValues`、`pickerKind/pickerOwnerField/objectType`、`visibleWhenField/visibleWhenValue`、`choices` 和 `references`；计算字段为 `computed:true`，不写回。

`elements` 提供 `anchors/transition/tracks/clips/marker/previewInput/blackboard`；轨道和片段模板带 `kind/clrType/value/shape`。输入节点类型、按键成员路径和各值类型成员从 `previewInput` 取。

整体 `availability.available=true` 不等于端口可用。例如宏输入节点缺少所属 Subgraph 上下文时，HTTP 仍为 200，`fields` 可读，但 `ports` 为 `{availability:{available:false,reason},errors:[...]}`，没有 `hasMore/items`。端口可用时才是 `{availability:{available:true,reason:null},hasMore,items,...}`。UI 保留该原因并在上下文补齐后重查，不能把它显示成“此节点没有端口”。

### 保存、编译与采用

```json
{
  "content": {
    "files": [{ "path": "graphs/<id>.json", "content": "<完整 JSON 原文>" }],
    "removedFiles": ["graphs/<已删除 id>.json"]
  },
  "preview": null
}
```

`files` 可只包含本次修改或新建的文件，删除必须列入 `removedFiles`；不能因未出现在 `files` 就推断删除。可选 `preview` 为 `{requestId,liveId,scene,sceneRevision,source,channels}`；可选 `job` 是下文完整 `StartPlayerJobRequest`。

| 保存结果 | 必须处理的回执 |
| --- | --- |
| 技能内容成功 | HTTP 200，`saved:true,compilationState:"succeeded",content,compilation,preview,jobError`；`compilation` 含 `compilationKey,sourceRevision,artifactReceiptPath,reused` |
| 仅输入闭包修改／场景方案 | HTTP 200，`saved:true,compilationState:"not-required",content,inputs,preview,job`；没有技能 `compilation` |
| 作者格式或编译拒绝 | HTTP 422，`saved:true,content,stage,compilationState`，附 `diagnostic` 或原 `error/output/exitCode`；文件已经落盘，不能展示为“没有保存” |

技能刷新回执单独使用 `stage=accepted/adopted/failed/unavailable`，并提供 `statusPath`；通过 `GET /api/preview/refreshes/{id}` 继续读取原请求。只有实际载入 ACK 为 applied 才为 adopted。只改输入时，宿主更新引用本目录方案的实际输入修订后载入或提交作业；包含技能内容的保存不能直接以旧场景包自动启动新档案，回执会给出 `jobError`。

`changes` 提供外部修改观察；通用 PUT 当前没有 `expectedRevision` 或 `If-Match` 参数，不能宣称它提供服务端乐观锁。引用、私有图与 Timeline 的新增删除应放在同一次作者事务的完整变更集合中。

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
| `GET /api/preview/live/{id}/frame.jpg` | 无 | JPEG，响应头 `X-Preview-Source` 是本图的完整 JSON 采样身份 |
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

命令阶段为 `sent/submitted/applied/rejected/not-applied`。HTTP 成功及 submitted 只表明已提交；UI 使用实际 ACK 的 `ok/error/content/clock/playerBuildId` 确认结果。状态还区分 Player 的 `idle/loading/paused/playing/stepping/seeking/failed`，不能与进程 `starting/running/exited` 混为一个枚举。

### 时钟与画面身份

实时采样与档案使用同一公共 `PreviewFrameSource`，完整字段见下文公共合同表；执行身份取 `execution`，载入和画面分别保留 `loadCommandId/sequence`，内容保留正式 `content` 及绑定修订。实际位置、秒数和显示名取 `clock.position.hostFrame/seconds/positionDisplayName`；请求位置独立保存在 `clock.requestedPosition`。网页不解读运行端时钟种类或换算帧率。

`Live/status.player.transport` 为可空 `PreviewTransportAction[]`，每项 `{action,available,reason,target}`，`target` 可为 null 或 `{hostFrame}`。旧运行端未提供资格时，Host 保留实际状态和来源，返回 `transport=null` 与明确 `transportUnavailableReason`，不能补造数组或阻断其它实例目录；网页显示原原因并禁用控制。数组存在时，`seek` 描述任意位置定位资格，`first/previous/next/last` 的目标由 Host 按原规则提供，`play/pause/step` 同样使用实际状态的资格。网页不从时钟种类推断；上一帧、首末帧只使用正式目标，下一帧仍发原 `step,count:1`。纯 Timeline 不制造角色或 Quantum CRC。

画面 WS 文本消息为 `status/frames/ack/pick/error/extension`；状态和专用扩展保留 `rawJson`。二进制消息格式为 `[4字节 little-endian 元数据长度][UTF-8 JSON][图像字节]`，元数据含 `hostFrame,source,width,height,flipY,worldToClip,mediaType`。直接复用 `openViewer` 解析，不能在浏览器消费 Native Player 的内部二进制头。

HTTP Live 与 WS `status.live` 共用同一完整读取器，均解码 `player.source/transport`、`latestSample` 与实际内容／绑定身份；Live 顶层没有 `rawJson`，原状态文本位于 `player.rawJson`。HTTP 和 WS 的 ACK 也共用读取器，保留 Host 的 `clock/content/source/hostFrame/rawJson`，允许正式空值，不在客户端重建帧号或原始记录。文本与视频均在原 `openViewer` 边界解码；协议字段缺失会清除当前时钟、目标与画面并进入已有错误记录，不继续显示旧样本为当前成功结果。

新 Load 后清除旧画面；接收图像时核对该图自身的 Load、Player build、内容／绑定修订及对应时钟。图片角标显示图片自身采样，不借另一次状态消息的帧号。只有 `frame.jpg` 的图像与 `X-Preview-Source` 或同一 WS 包里的图像与 source 能组成同次画面。

状态、画面和命令通过同一来源比较：执行身份、构建／Editor程序集、Load、内容、文档／绑定及场景／输入修订均参与；实际时钟和采样序号各自保留，不把连续采样误作新 Load。来源改变、画面连接关闭或新 Load 开始时清除旧时钟、定位目标、回执和画面；旧来源状态及迟到回执不得关联新目标。既有 HTTP 列表刷新只在 WS 尚未打开时供同一选中执行初始化状态，不增轮询或第二次取图。最后命令保留完整原回执；submitted 后到达的同 commandId WS ACK 更新同一条记录，仍需核对实际内容与来源。

## 正式作业与档案

| 方法与路径 | 请求／用途 | 响应合同 |
| --- | --- | --- |
| `POST /api/preview/player-jobs/creation-options` | `{requestId,source,width,height}`；先取得正式默认范围及输出目录 | `requestId,firstSdkFrame,lastSdkFrame,captureFrames,outputDirectory,width,height,quality,channels,mode,frameRangeBasis` |
| `POST /api/preview/player-jobs` | 完整 `StartPlayerJobRequest` | `PlayerJobReceipt`，包含原请求、job/build 身份、stage 和 `statusPath` |
| `GET /api/preview/player-jobs` | 作业列表与队列情况 | `capacity,concurrency,queued,archiveCacheCapacity,items` |
| `GET /api/preview/player-jobs/{id}` | 当前作业及可用档案 | `{receipt,archive}` |
| `POST /api/preview/player-jobs/{id}/cancel` | `{}` | `{jobId,cancellationRequested,jobStage,reason}`；发出取消不等于已结束 |
| `GET /api/preview/player-jobs/{id}/archive` | 原正式档案清单 | 原 Host JSON；网页只读取已消费的 `jobId,inputMode`，不要求项目协议名、Player 构建号或私有时钟 |
| `GET /api/preview/player-jobs/{id}/files/{path}` | 清单内的原文件路径 | 原文件正文 |
| `GET /api/preview/player-jobs/{id}/summary` | 档案概况 | `PlayerSummary`，含实际帧范围、场景／输入／内容修订及通道可用性 |

作业请求包含 `requestId,playerBuildId,scene,sceneRevision,source,firstSdkFrame,lastSdkFrame,captureFrames,outputDirectory,width,height,quality,channels,mode`；新作业 `mode` 为 `real`，输出目录使用 creation-options 返回的 D 盘路径。`source` 与实际场景输入选项一致。请求身份、Live 身份、构建身份和档案身份各自保存，不能把 Live ID 当作 job ID。

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

## 动画资源与专用扩展

| 方法与路径 | 参数／请求 | 响应 |
| --- | --- | --- |
| `GET /api/resources/animations` | 无；Host 需配置 AnimationLibrary | `AnimationLibrary` 正式清单与内容 SHA256 |
| `GET /api/resources/animations/file/{hash}` | 清单实际哈希；先读取清单 | glb，支持 Range 和 ETag |
| `POST /api/authoring/wall-contact/read` | `{sourceAssetId,graphId,nodeId}` | 正式来源 `WallSource`，含 `revision/values` |
| `POST /api/authoring/wall-contact/save` | `{target:{sourceAssetId,graphId,nodeId},expectedRevision,values:[{id,value}]}` | 实际保存后的来源与修订；走原目标 Unity 工具 |

扶墙调参运行命令沿同一个 Live `/commands`，使用已登记的 `type:"wall-contact"` 及 `read/observe/apply/wall` 操作；完整请求、参数版本、实例身份、结果和重查入口在 [wall-contact/protocol.ts](web/src/panels/wall-contact/protocol.ts)。运行参数采用和保存正式来源是两个操作，各自按原回执确认；这里不新增另一条运行或保存路径。

`WS /api/preview/live/{id}/player?token=...` 是 Native Player 的回连入口，供宿主启动的 Player 使用，UI 使用 `/view`，不持有或转发该 token。

## 错误、取消与当前能力边界

公共异常响应为 `{error}`：格式或参数错误 HTTP 400，找不到文件／身份 404，状态或修订冲突 409，其它宿主异常 500。作者严格读取／编译可返回前述 422，含 `code/message/errors` 或保存分支回执。动画资源缺失为 409、清单无效为 422、未知资源版本为 404；保留其具体 `code/message`。

字段／端口部分不可用可能仍是 HTTP 200；运行、刷新和作业也有各自业务拒绝阶段。UI 不能只看 HTTP 2xx。客户端 [http.ts](web/src/api/http.ts) 保留 `ApiError.status/reason/raw/path`；切换文档和查询上下文时取消旧只读请求，原命令结果未确认则按原 commandId 重查。

目前没有单独的技能目录复制／重命名／删除协议，也没有独立 Timeline 作者目录的 CRUD 协议。技能私有 Timeline 随技能 JSON 保存；已发布独立 Timeline 的目录、Load、时钟和画面由上文接口提供。场景方案只开放表中三种 `queries`，没有技能专属 `document/references/blackboards` 路由。UI 有新增业务操作时，应先补对应正式接口及类型，不在本地硬编码另一套作者规则。

当前项目预览使用 Player 作业、档案 HTTP 读取和 Live WebSocket；没有供本前端调用的旧 `/api/preview/runs/*` 控制或 SSE 流。运行控制不由网页自行解释技能节点实现。
