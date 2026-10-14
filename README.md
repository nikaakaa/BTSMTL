# BTSMTL 编排与项目玩法源码

本仓库分别提供编排、玩法和共享基础三个源码包。BTSMTL 安排技能、图和 Timeline 的执行顺序；角色运动、攻击判定、属性与效果计算由 Gameplay 实现。网络后端按自己的机制推进同一份玩法代码。

| 包 | 负责什么 |
| --- | --- |
| `com.btsmtl` | 图、Timeline、黑板作者入口、Unity 编辑器、诊断和现有编排适配 |
| `com.thirdperson.gameplay` | 角色运动、攻击与命中处理、属性与效果、玩法会话及相应运行数据 |
| `com.thirdperson.shared` | 两边使用的身份、值声明、诊断与性能合同、Luban 基础读写和编译期值生成支持 |
| `com.btsmtl.network.server-authoritative` | 可选服务器权威模型的预测、纠正、历史与传输 |

例如一次攻击由 BTSMTL 安排何时发起和结束，Gameplay 接收请求并处理攻击查询及结果，属性与效果模块按原事务改变状态；结果返回编排供后续分支使用。BTSMTL 不承担命中或伤害算法。包移动保留原算法、命名空间、程序集名称、资源 GUID 与当前文件内容。

## Unity 接入

仓库与 `3C` 放在同一父目录。3C 客户端的 `Packages/manifest.json` 显式安装三个本地包：

```json
"com.btsmtl": "file:../../../../../BTSMTL/Packages/com.btsmtl",
"com.thirdperson.gameplay": "file:../../../../../BTSMTL/Packages/com.thirdperson.gameplay",
"com.thirdperson.shared": "file:../../../../../BTSMTL/Packages/com.thirdperson.shared"
```

Unity 通过各包原有 asmdef 编译同一份源码。3C 的 Assets 中不保留副本；角色资产、生成的 C# authoring、Workbench 角色装配、表现和产品发布入口仍由 3C 持有。

需要服务器权威模型时，宿主另外安装 `com.btsmtl.network.server-authoritative`。该包直接依赖 Gameplay 和 Shared；Q3 的项目运行包与 Quantum FP 数学包也直接声明实际使用的 Gameplay、Shared 依赖。需要 BTSMTL Timeline 或诊断的现有 Unity 网络产品保留相应编排依赖。Photon Quantum SDK 仍由原安装目录提供，不随本次分包移动或修改。

## 普通 .NET 接入

本仓库的 `BTSMTL.sln` 与 `Projects` 工程仍使用原工程名，源码 Include 改为所属包的新路径。Unity asmdef 与普通 .NET 工程编译同一份实现，不读取 3C 的 Assets。

```powershell
try {
    dotnet build BTSMTL.sln --disable-build-servers /nr:false /p:UseSharedCompilation=false
} finally {
    dotnet build-server shutdown
}
```

服务端按实际数值域引用 `Projects/ThirdPersonSimulation.Fixed` 或 `Projects/ThirdPersonSimulation.Float32`；需要 Timeline 时再引用 `Projects/ThirdPersonSimulation.Timeline`。具体网络工程仍单独引用 `Projects/ThirdPersonSimulation.ServerAuthoritative` 或 `Projects/ThirdPersonSimulation.ServerAuthoritative.Transport`。

服务端提供输入、逻辑时间、角色和内容绑定，原运行器产生并提交或丢弃结果。动画、场景、声音和资源解析继续由 Unity 接入处理。分包不改变回放、存档、网络快照的按需装配责任，也不表示 Quantum FP 角色执行已完成。

## 源码位置与依赖

| 源码位置 | 内容 |
| --- | --- |
| `Packages/com.btsmtl/Scripts`、`Authoring`、`EventGraphs` | 图节点、作者合同和 EventGraph 接入 |
| `Packages/com.btsmtl/Timeline` | 共用 Timeline 运行核心、Unity 作者对象、编辑器与现有适配 |
| `Packages/com.btsmtl/BlackboardConfiguration` | 黑板默认配置与生成接入，Luban 基础库已移到 Shared |
| `Packages/com.btsmtl/Diagnostics`、`Editor` | 编排诊断、编辑窗口和界面资源 |
| `Packages/com.thirdperson.gameplay/Runtime/Simulation` | Core、Fixed、Float32 现有运行程序集 |
| `Packages/com.thirdperson.gameplay/Runtime/Physics/BepuQueries` | 既有物理查询源码与许可 |
| `Packages/com.thirdperson.gameplay/Configuration` | Gameplay 配置合同、Reader 和对应生成输出 |
| `Packages/com.thirdperson.shared/Runtime` | 共享合同、性能合同及 Luban 基础库 |
| `Packages/com.thirdperson.shared/Analyzers` | 原编译期生成器的 Unity 安装产物 |

当前 `BTSMTL`、`BTSMTL.Timeline.Runtime` 等程序集使用 Gameplay 提供的 Core、Fixed、Float32 数据与调用接口，所以 `com.btsmtl` 显式依赖 Gameplay 和 Shared；Gameplay 仅依赖 Shared，不依赖 BTSMTL 作者包。Core 中现有的通用控制流与领域装配仍处于同一程序集，本次保留这一事实，不把目录移动描述成通用编排核心已经完全独立。

Unity 作者包继续要求宿主提供 NodeCanvas、FlowCanvas、ParadoxNotion 与 Slate；诊断采样注解由 Shared 的正式包依赖提供。包内 Unity 程序集不引用 3C 的 Client、Gameplay、Camera.Runtime 或 Simulation.Unity。

## 开发与检查边界

- 每份源码只有一个正式位置，不保留旧目录副本或备用读取路径。
- 移动源码和资源保留 `.meta` GUID，已有暂存与未暂存修改跟随原文件迁移。
- 构建关闭 build server 和共享编译，结束或失败立即执行 shutdown。
- 本次分包的源码、编译和 Unity 采用结果分别记录；包结构完成不能代替角色、战斗或联机验收。
