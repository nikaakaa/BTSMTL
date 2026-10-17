# Gameplay

本包提供与 Frame 无关的玩法规则、运动计算、攻击几何和 Gameplay 配置。角色运行状态、阶段系统与会话由 3C `com.thirdperson.network.quantum3` 的 Quantum 宿主持有；BTSMTL 负责安排何时发出请求、如何根据结果继续执行，不依赖本包。

`Runtime/Simulation` 编为 `ThirdPersonSimulation.Core`，只保留 Quantum 实际使用的 GE、属性、Tag、动作与装备的布局和规则类型，仅引用 Shared 与 Gameplay.Configuration。`Runtime/Movement` 是运动纯计算，`MovementSerialization` 提供其 MemoryPack 编码，`MovementTimeline` 持有 MotionCurve 轨道、Clip 与 `RootMotionCurveAsset` 作者类型。`Runtime/AttackGeometry` 提供 FP 扫掠查询与攻击几何准备。`Configuration` 提供配置合同和 Reader，`Editor/Compilation` 提供技能编译合同。Unity 和仓库 `Projects` 中的普通 .NET 工程读取同一份源码。

包依赖声明 `com.thirdperson.shared`、`com.btsmtl`、`com.thirdperson.gameplay.contracts` 与 `com.thirdperson.numerics.quantum`；宿主提供官方 `Quantum.Deterministic.dll`。FP 数值身份为 `quantum-fp-q48.16`，旧 Q32.32 Raw 不能按新精度解释，作者内容须重新降低并发布。
