# Gameplay

本包负责项目的角色运动、战斗、属性与效果计算，以及玩法会话和对应运行数据。BTSMTL 负责安排何时发出请求、如何根据结果继续执行；网络后端负责输入同步、推进和状态恢复。

`Runtime/Simulation` 包含 `ThirdPersonSimulation.Core`、`ThirdPersonSimulation.FPGameplay` 和 `ThirdPersonSimulation.Float32`。原 Fixed 玩法逐文件迁入 `FPGameplay`，标量、向量和朝向直接使用官方 Quantum FP。`Runtime/Physics/BepuQueries` 保留原物理查询与许可，`Configuration` 提供 Gameplay 配置合同和 Reader。Unity 和仓库 `Projects` 中的普通 .NET 工程读取同一份源码。

包依赖声明使用 `com.thirdperson.shared`；3C 宿主同时安装 `com.thirdperson.numerics.quantum` 并提供官方 `Quantum.Deterministic.dll`，供 FP 目标装配数学策略。安装 Gameplay 不要求安装 BTSMTL 作者编辑器，也不选择具体网络后端。Core 持有供编排调用的通用控制流和数据类型。

FP 数值身份统一为 `quantum-fp-q48.16`。旧 Q32.32 Raw 不能按新精度解释，作者内容必须重新降低并发布。本次迁移仍在接合公共 Timeline／控制合同、内容发布及快照生成；源码迁入新目录不代表这些阶段已经通过检查。
