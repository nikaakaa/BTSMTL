# BTSMTL 源码包

BTSMTL 的唯一源码仓库。通用合同、图／技能执行、Fixed／Float32 数值域、Timeline 共用运行核心及其依赖与 Unity 作者工具、宿主和诊断工具统一归本仓库。保留原命名空间、程序集名称及资源 GUID。

## Unity 接入

仓库与 `3C` 放在同一父目录。3C 客户端的 `Packages/manifest.json` 引用：

```json
"com.btsmtl": "file:../../../../../BTSMTL/Packages/com.btsmtl"
```

Unity 通过包内原有 asmdef 编译源码。编辑源码直接修改本仓库，3C 的 Assets 中不保留另一份 BTSMTL。角色资产、生成的 C# authoring、Workbench 角色装配及产品发布入口仍由 3C 持有。

## 普通 .NET 接入

本仓库提供独立的 `BTSMTL.sln` 与 `Projects` 下的 .NET 8 工程，不读取 3C 的 Assets 或源码。客户端的 Unity asmdef 与服务端 .NET 工程编译同一份运行源码。

```powershell
dotnet build BTSMTL.sln --disable-build-servers /nr:false /p:UseSharedCompilation=false
dotnet build-server shutdown
```

服务端按实际数值域引用 `Projects/ThirdPersonSimulation.Fixed` 或 `ThirdPersonSimulation.Float32`，需要 Timeline 时引用 `Projects/ThirdPersonSimulation.Timeline/ThirdPersonSimulation.Timeline.csproj`。项目引用自动带入本仓库的公共合同、Core、数值与物理依赖及编译期生成器。旧 3C Core／Fixed 的 Portable 包装工程已移除，3C 的调用工程直接引用本仓库项目。

服务端权威模拟与传输工程也位于 `Projects/ThirdPersonSimulation.ServerAuthoritative`、`Projects/ThirdPersonSimulation.ServerAuthoritative.Transport`，编译包内对应 Network 源码。3C 网络产品引用这些正式项目；独立解决方案共包含 8 个运行库与编译期生成器。

服务端提供编译后的图／Timeline 内容、逻辑时间、输入、播放控制和 TreeClip 调用者；共用运行器执行并提交或丢弃候选，输出玩法结果、播放结果和快照，由宿主消费。服务端不需要 UnityEngine。普通 .NET 编译范围不包含 Unity 作者对象、Editor 或 Unity 组件；动画、场景和声音表现仍由 Unity 客户端消费。

## 源码职责

| 目录 | 职责 |
| --- | --- |
| `Foundation` | 公共内容／执行合同、诊断及性能合同；不是某一客户端的业务实现 |
| `Runtime/Simulation` | 图、状态机、技能与动作执行合同和运行实现，Fixed／Float32 数值域 |
| `Runtime/Physics/BepuQueries` | 运行数值与物理查询所需的既有第三方源码及许可 |
| `Timeline/Runtime` | 不引用 Unity 的时间推进、内容求值、TreeClip 生命周期、快照及普通 .NET 适配器 |
| `Timeline/Scripts` | Unity Timeline 作者对象、树节点和运行宿主装配 |
| `Timeline/Unity` | 场景表现组件 |
| `Timeline/Editor` | Timeline 与 TreeClip 编辑器 |
| `Authoring`、`Scripts`、`EventGraphs` | 共用作者合同、图节点及 Unity EventGraph 接入 |
| `Authoring/RootMotion`、`Timeline/Scripts/CameraEffectAsset.cs` | Unity 曲线资源与相机效果资源合同；具体角色和相机实现由宿主提供 |
| `Diagnostics`、`Editor` | 运行诊断、编辑窗口和界面资源 |
| 仓库 `Projects`、`Tools/CanonicalSerialization.Generator` | 独立 .NET 工程及编译期序列化生成器；Unity 分析器产物位于包内 `Analyzers` |

## 依赖边界

共用 Timeline 核心依赖的 `ThirdPerson.Shared.Contracts`、`ThirdPersonSimulation.Core`、`ThirdPersonSimulation.Fixed`、`ThirdPersonSimulation.Float32` 和 `ThirdPerson.BepuQueries` 现均由本仓库持有。`ThirdPerson` 是保留的公开程序集／命名空间身份，不再表示源码属于 3C。生成器同样归本仓库，独立构建不需要 3C 项目。

包内 Unity 程序集不引用 3C 的 Client、Gameplay、Camera.Runtime 或 Simulation.Unity。宿主需提供 NodeCanvas、FlowCanvas、ParadoxNotion、Slate 与诊断采样注解程序集；`com.unity.editorcoroutines` 与 `com.unity.ugui` 由包声明。3C 继续持有角色资产、领域装配、Gameplay／相机／动画表现、产品发布与网络产品入口。

Unity 宿主通过原 `ITimelineWorkspaceModeController` 装配预览与 `ITimelineEditorAuthoringHost`。窗口打开时把作者宿主传入 `TimelineEditorOpenRequest`；包内编辑器请求创建／打开图、清理所属子资产或读取时间网格帧率，具体图类型和角色 Pipeline 由宿主处理。3C 的实现为 `CharacterTimelineEditorAuthoringHost`，调用原技能图工厂与资产所有权规则。包不再直接引用 3C 的 Skill 作者类型。

相机效果资源自己声明 `ResourceKind`、身份和优先级；Timeline 消费同一资源合同，不通过 3C 的具体相机资产类型分支选择行为。资源引用、曲线资产和原 meta GUID 保留。

本次已完成独立解决方案编译和既有 15 项 Timeline .NET 测试；未启动完整服务器或 Unity 场景，编译与核心测试不代替产品运行验收。

## 开发约束

- Unity 与普通 .NET 消费同一核心源码，不新增另一套推进算法或旧路径副本。
- 移动源码和资源时保留 `.meta` GUID；高频运行链路保持 0 GC。
- 用户已有的未提交修改保持未提交，避免迁移提交混入并行开发内容。
- 编译使用 `--disable-build-servers /nr:false /p:UseSharedCompilation=false`，结束后执行 `dotnet build-server shutdown`。
