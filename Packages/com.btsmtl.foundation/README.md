# BTSMTL Foundation

本包提供 BTSMTL 与其宿主共同使用的中立基础类型：身份校验与稳定哈希、`EventId`、`OperationHandle`、数值配置与数学合同、Float32 数值实现、曲线、排序、录制状态读写，以及编译期值生成特性和生成器的 Unity 安装产物。

`Runtime` 编为 `BTSMTL.Foundation`，不引用 Unity 引擎、BTSMTL 其它程序集或任何宿主实现。生成器正式源码与构建入口在仓库 `Tools/ValueGeneration.Generator`，`Analyzers` 只保存其安装产物。

现有类型名、命名空间 `ThirdPersonSimulation` 与 `.meta` GUID 随迁移保留，后续统一改名另行处理。
