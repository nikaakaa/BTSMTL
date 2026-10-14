# Gameplay

本包负责项目的角色运动、战斗、属性与效果计算，以及玩法会话和对应运行数据。BTSMTL 负责安排何时发出请求、如何根据结果继续执行；网络后端负责输入同步、推进和状态恢复。

`Runtime/Simulation` 保留 `ThirdPersonSimulation.Core`、`ThirdPersonSimulation.Fixed`、`ThirdPersonSimulation.Float32` 的公开身份和现有调用关系。`Runtime/Physics/BepuQueries` 保留原物理查询与许可，`Configuration` 提供 Gameplay 配置合同和 Reader。Unity 和仓库 `Projects` 中的普通 .NET 工程读取同一份源码。

本包只依赖 `com.thirdperson.shared`。安装 Gameplay 不要求安装 BTSMTL 作者编辑器，也不选择具体网络后端。现有 Core 还持有供编排调用的通用控制流和数据类型；本次分包未重写这些算法或声称完成其进一步职责拆分。

Quantum FP 数学绑定由独立数值包提供。目录与依赖迁移不代表当前 Fixed 角色执行已改为完整 FP 执行。
