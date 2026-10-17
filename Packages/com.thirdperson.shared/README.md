# Shared

本包提供项目玩法与表现共同使用的角色与世界身份、动画与控制 trace、性能与埋点合同、表现请求，以及 Luban 基础读写库。

身份校验、稳定哈希、数值合同、录制状态与编译期值生成特性等中立基础已迁入 `com.btsmtl.foundation`，本包通过包依赖与程序集引用使用它们。现有程序集名、类型名、命名空间与 `.meta` GUID 保留。

本包不引用 BTSMTL 编排、Gameplay 或具体网络后端。共同使用的诊断采样注解通过 `com.kk.generated-diagnostic-sampling` 依赖提供。输入录制与性能工具直接读取本包中原有的共享声明。
