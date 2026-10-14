# Shared

本包提供编排与 Gameplay 共同使用的身份、值声明、序列化声明、诊断与性能合同，以及 Luban 基础读写库和原编译期生成器的 Unity 安装产物。

运行合同位于 `Runtime`，生成器安装产物位于 `Analyzers`。生成器的正式源码与构建入口继续使用仓库 `Tools`；不增加另一套生成实现。现有程序集名、类型名和 `.meta` GUID 保留。

本包不引用 BTSMTL、Gameplay 或具体网络后端。共同使用的诊断采样注解通过 `com.kk.generated-diagnostic-sampling` 依赖提供。输入录制与性能工具直接读取本包中原有的共享声明。
