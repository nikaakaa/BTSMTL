# BTSMTL 编排与项目玩法源码

本仓库提供通用编排、领域合同、独立玩法算法、共享基础与冷数据编码。BTSMTL 安排 Skill／FSM、Timeline 和 EventGraph 的执行顺序；宿主提供状态内存、数值类型、数学策略和领域扩展，接收编排输出。Quantum 原生玩法及 Unity 表现装配由宿主项目持有。

| 包 | 负责什么 |
| --- | --- |
| `com.btsmtl.foundation` | 编排与宿主共用的中立基础：身份与稳定哈希、数值合同与 Float32 实现、曲线、录制状态和编译期值生成，不依赖 Unity 引擎与任何宿主 |
| `com.btsmtl` | 唯一泛型编排执行器、通用图编译层、整数 Timeline、图与黑板作者工具、EventGraph 和诊断 |
| `com.thirdperson.gameplay.contracts` | 领域扩展身份、版本、端口、只读参数与枚举 |
| `com.thirdperson.gameplay` | 独立运动算法、攻击几何、玩法配置及仍在清理的旧冷合同 |
| `com.thirdperson.shared` | 项目玩法与表现共用的角色身份、动画与控制 trace、性能合同、表现请求和 Luban 基础读写，依赖 Foundation |
| `com.thirdperson.numerics.quantum` | Quantum 宿主的 FP 数学策略、作者数值转换和时间采样边界 |
| `com.thirdperson.memorypack` | 从原成员生成按需使用的外部冷数据编码 |

例如一次攻击由 BTSMTL 安排何时发起和结束，Quantum 领域扩展读取当前 Frame，处理攻击查询、命中与属性效果；表现请求交给 Unity 宿主消费。编排时间使用整数 Tick 与 Q48.16 整数进度、倍率和余数；黑板值、比较与曲线采样值由宿主通过泛型和数学策略决定。旧 FPGameplay／Float32 完整玩法执行器、Snapshot 包和服务器权威产品已直接删除。

## HTML 工作台原型

[BTSMTL.WebWorkbench](Tools/BTSMTL.WebWorkbench/README.md) 提供独立网页节点编辑和时间轴交互草稿。网页通过本地 HTTP 调用原 .NET 编译器，当前开放七种基础流程节点和布尔输入；不需要 Unity 或宿主角色配置。图编译已接通，时间轴仍是编辑草稿，完整运行与发布不在本版范围。启动方式、操作和检查范围由工具 README 维护。

## Unity 接入

仓库与 `3C` 放在同一父目录。3C 客户端的 `Packages/manifest.json` 显式安装以下本地包：

```json
"com.btsmtl": "file:../../../../../BTSMTL/Packages/com.btsmtl",
"com.btsmtl.foundation": "file:../../../../../BTSMTL/Packages/com.btsmtl.foundation",
"com.thirdperson.gameplay": "file:../../../../../BTSMTL/Packages/com.thirdperson.gameplay",
"com.thirdperson.gameplay.contracts": "file:../../../../../BTSMTL/Packages/com.thirdperson.gameplay.contracts",
"com.thirdperson.memorypack": "file:../../../../../BTSMTL/Packages/com.thirdperson.memorypack",
"com.thirdperson.numerics.quantum": "file:../../../../../BTSMTL/Packages/com.thirdperson.numerics.quantum",
"com.thirdperson.shared": "file:../../../../../BTSMTL/Packages/com.thirdperson.shared"
```

Unity 通过各包原有 asmdef 编译同一份源码。3C 的 Assets 中不保留副本；角色资产、生成的 C# authoring、Workbench 角色装配、表现和产品发布入口仍由 3C 持有。

`com.btsmtl/Preview` 提供 Unity 预览的连接、播放控制、按面板尺寸采集画面和检查点框架。项目通过 `IPreviewProjectAdapter` 提供载入、单步、输入、提交帧与存读档，沿原游戏执行器输出实际观察。默认组件状态与可选 Animancer8 模块的恢复能力、限制和自检步骤见[预览播放器接入 skill](.agents/skills/btsmtl-preview/SKILL.md)。源码接入不代替项目实际构建、采用和恢复自检。

`com.btsmtl` 的编排、编译与诊断以 Foundation 为中立数据基础，不反向依赖项目 Gameplay。Unity 图作者接入原 FlowCanvas／NodeCanvas；`BTSMTL.Preview` 使用 Unity 引擎 API，可选 Animancer 模块另引用 `Kybernetik.Animancer`，基础预览模块不要求安装它。包清单的必需依赖仍为 Foundation 与 Unity 官方包。

诊断只定义编排自身的图、状态机、Timeline、黑板与数值通道，另留一个中立的 `Host` 通道；宿主事件经 `RuntimeHostTraceKinds.Register(编号, 名称, 采集级别)` 登记，编号写入预览事件文件，只追加不复用。预览事件文件为 Version 12、每条 600 字节，播放来源包含调用方播放 ID 与调用元素身份。依赖守护 `BTSMTL_NO_GAMEPLAY_SDK` 禁止 BTSMTL 程序集引用任何 `ThirdPerson*` 程序集或 `com.thirdperson.*` 包。Gameplay、领域合同、Quantum 数学与 MemoryPack 单向引用 BTSMTL；BTSMTL 不引用这些宿主实现。Photon Quantum SDK 由使用 Quantum 的宿主提供。包级依赖解除与全部作者外围通过依赖守护分别检查。

## 通用图子图与分组

`BtsmtlMacroGraph` 是通用事件图与领域图（如技能图）共用的子图资产，声明稳定身份、宿主准入、参数接口、输入／输出锚点及局部变量。`MacroNodeWrapper` 引用共享资产，或引用由所属图拥有的私有子图；`Graphs/Editor/BtsmtlMacroCompilation` 按调用位置展开参数与调用帧，宿主继续确认节点和输入可用。共享子图的角色历史值可由调用方显式传入、接回并保存。

领域经 `BtsmtlMacroHost` 子类声明准入，并可覆盖节点 Inspector、值输入、节点菜单与创建时锚点。作者通过 `ConfigureInterface` 及同一接口事务修改参数和调用端口；接口未变时不改写，事务只标脏实际写入的图，调用方资产不被连带改写。C# authoring 的独立子图根、正式导出与生成仍由 3C 持有；共享引用不进入调用方写集，私有子图随所属根重建和退役。`ConfigureCanvasGroups` 整体配置名称、范围、颜色及自动包围，分组随导出生成保存，只影响编辑画布。

## Timeline 作者编辑与预览

`Compilation/Analysis/OrchestrationStaticQuery` 从正式语义操作、连线、来源与内容引用读取状态绑定、条件读取、Clip 状态提供者和嵌套 Timeline 调用点；领域经 `IOrchestrationStateKeyDeclarations` 声明自己的状态键。`OrchestrationStateTransitionWindows` 计算状态转移的可走时段，`TimelineStaticChain` 结合输入成立区间和逐条运行数据假设推导状态路线及并行分支，不执行作者图，不保存另一份作者配置。状态转移使用正式 `Priority → Order → SourcePortId → IdentityId` 比较器和发布身份表；跨状态的持续行为由作者的并行结构表达。

Timeline 窗口是唯一预览入口，提供快速／真实两档。输入轨支持持续值的按下与松开、脉冲请求及录制导入；具名输入方案按入口所属内容与 Timeline 作者身份归属，可与资产一起提交。并排路线显示每次播放、循环圈和并行分支；编辑时间轴跟随选定的那次播放。输入提示区分有因果依据的“接上”和仅有读取关系的“相关”。

快速档由 `TimelineAssetPreviewHost` 持有独立 `PreviewSceneStage`、准备内容和游标；`ITimelineAssetPreviewProvider` 提供配置、专用场景、输入元数据和真实会话入口，`ITimelineAssetPreviewInstance` 装配领域，`ITimelineDomainPreview` 消费同一时刻全部生效播放的正式目标采样。框架不解释领域计算。真实档沿宿主的正式会话接收相同输入日程，窗口内提供播放、定位、运行值和显式内容应用。切档先释放原候选与 Stage，异步准备归本次取消令牌所有。

真实路线使用正式状态进入／退出及 `StateTransitionSelected`，播放按状态激活、执行代次、实例、分支和纪元归属。两档画面均叠加在原编辑时间轴；真实时间换算只读对应播放的逐 Tick 游标，停顿吸附写明原因，缺记录时拒绝定位与编辑。Clip／TreeClip 移动和裁剪沿原 `ITimelineTimeRangePreview` 提交，一手势一 Undo，完整候选采用后替换结果。输入或作者内容改变后，旧真实结果标为过期，重演并匹配版本后才与快速路线比较。

嵌套子视图挂在调用轨道下，默认收起，父视图只读；双击调用 Clip 或子视图进入原 Timeline，面包屑返回时恢复选中、滚动、缩放和展开状态。静态查询写明调用偏移、播放模式和寿命，不能确定时刻的调用交给真实会话确认；真实归属只用记录中的父播放 ID 和调用元素身份。快速档把可确定的嵌套贡献交给同一领域扩展。当前编译闭包拒绝递归引用，视图也在重复路径处截止展开。

## 普通 .NET 接入

`BTSMTL.sln` 装配 Foundation、Shared、编排合同、运行时与通用编译层、整数 Timeline、Luban、黑板默认配置与两个生成工具；Unity asmdef 与普通 .NET 工程编译同一份实现。这些编排工程不需要 Photon DLL。正式 solution 构建先执行依赖守护，源码或引用方向违规时直接失败。

```powershell
try {
    dotnet build BTSMTL.sln --disable-build-servers /nr:false /p:UseSharedCompilation=false
} finally {
    dotnet build-server shutdown
}
```

需要编排的宿主引用 `Projects/BTSMTL.Orchestration.Runtime`，需要整数 Timeline 再引用 `Projects/ThirdPersonSimulation.Timeline`（程序集名 `BTSMTL.Timeline.Runtime`）。Quantum 宿主另引用数值策略和所需独立领域算法，并通过 `QuantumDeterministicPath` 提供官方 DLL。

作者工具引用 `Projects/BTSMTL.Compilation`，将通用语义操作、常量、连线和状态声明编译为 `OrchestrationGraphContent`。该程序集不引用 Unity、Gameplay、Quantum 或 MemoryPack。宿主经 `BtsmtlOrchestrationCompilationDomain` 提供领域状态规则、扩展内容编码与 ActionInstance 黑板扩展，数值转换经 `IBtsmtlOrchestrationNumericLowering` 提供。Unity 资产读取、技能根和参数重编计划仍由宿主作者前端负责。

宿主提供输入、逻辑时间、内容和 roster 绑定，唯一执行器读写宿主提供的候选区段，由宿主采用或丢弃。历史、保存与恢复继续归实际后端；Quantum 使用 SDK Frame。动画、场景、声音和资源解析归 Unity 接入。编排与领域源码编译成功不代表产品入口、内容发布和实际运行已验收。

## 源码位置与依赖

| 源码位置 | 内容 |
| --- | --- |
| `Packages/com.btsmtl/Runtime/Orchestration` | 泛型编排合同、执行器、区段布局与输出 |
| `Packages/com.btsmtl/Compilation` | Editor 通用语义合同、值端口、依赖目录、状态规则及图编译器，普通 .NET 工程同源 |
| `Packages/com.btsmtl/Scripts`、`Authoring`、`Graphs` | 基础值节点、作者字段合同、通用图作者与 Canvas 编译、宿主接入 |
| `Packages/com.btsmtl/Timeline` | 整数 Timeline 运行核心、Unity 作者对象与编辑器 |
| `Packages/com.thirdperson.gameplay/BlackboardConfiguration` | Luban 格式的黑板默认配置读取合同（程序集 `ThirdPersonSimulation.BlackboardConfiguration`），属于项目，不随 BTSMTL 分发 |
| `Packages/com.btsmtl/Diagnostics`、`Editor` | 编排诊断、编辑窗口和界面资源 |
| `Packages/com.thirdperson.gameplay.contracts/Runtime` | 领域扩展合同与参数 |
| `Packages/com.thirdperson.gameplay/Runtime/Movement` | 唯一的确定性运动算法 |
| `Packages/com.thirdperson.gameplay/Runtime/Simulation` | 留存的配置、发布合同及待收齐的旧宿主合同 |
| `Packages/com.thirdperson.gameplay/Runtime/Physics/BepuQueries` | 既有物理查询源码与许可 |
| `Packages/com.thirdperson.gameplay/Configuration` | Gameplay 配置合同、Reader 和对应生成输出 |
| `Packages/com.btsmtl.foundation/Runtime` | 中立基础类型：身份、哈希、数值、曲线、排序、录制状态与值生成特性 |
| `Packages/com.btsmtl.foundation/Analyzers` | 值生成器的 Unity 安装产物，源码在 `Tools/ValueGeneration.Generator` |
| `Packages/com.thirdperson.shared/Runtime` | 项目共享合同、性能合同及 Luban 基础库 |
| `Packages/com.thirdperson.numerics.quantum` | Quantum 宿主数值策略 |
| `Packages/com.thirdperson.memorypack` | 按需外部冷数据编码 |

黑板生成工具 `Tools/BlackboardData.Generator` 依赖黑板配置协议，属于宿主工具，程序集名为 `ThirdPersonSimulation.BlackboardData.Generator`，宿主经 `com.thirdperson.gameplay` 包位置定位它；它读取宿主声明的数值类型、Profile、ABI、工厂和已转换默认值，不引用 FP 或具体玩法。3C 的 Gameplay 配置生成器位于宿主项目 `Tools/GameplayConfig/Generator`，由宿主正式脚本调用；它依赖玩法配置及 MemoryPack，属于宿主工具。

Unity 作者工具要求宿主提供 NodeCanvas、FlowCanvas、ParadoxNotion 与 Slate；其中图插件使用已适配 Shared 文本合同的版本。诊断采样注解由 Shared 的正式包依赖提供。Quantum 数学、运动算法和对应冷编码所需 Photon DLL 属于宿主侧依赖。

### 当前检查与未完成边界

编排核心、整数 Timeline、黑板工具及生成模块已经实际独立编译。依赖守护仍会报告 Timeline 作者外围等剩余玩法引用；完整 solution、Unity 导入、技能／动画数据重新发布与 Quantum 产品运行分别收口，不能由局部编译结果推定完成。

依赖守护位于 `Tools/DependencyGuard`，由 `Directory.Solution.targets` 接入正式构建，也可通过 `dotnet msbuild BTSMTL.sln /t:DependencyGuard /nr:false /p:UseSharedCompilation=false` 单独执行。详细路径、实际证据与历史成因维护在 3C 的 `docs/路径/编排与玩法分包.md`、`docs/路径/BTSMTL编排状态区段.md` 和 `docs/经验/BTSMTL跨仓库依赖方向.md`。

## 开发与检查边界

- 每份源码只有一个正式位置，不保留旧目录副本或备用读取路径。
- 移动源码和资源保留 `.meta` GUID，已有暂存与未暂存修改跟随原文件迁移。
- 构建关闭 build server 和共享编译，结束或失败立即执行 shutdown。
- 本次分包的源码、编译和 Unity 采用结果分别记录；包结构完成不能代替角色、战斗或联机验收。
