# ServerAuthoritative 可选网络包

本包拥有现有 ServerAuthoritativeHybrid 同步模型、预测与权威状态、协议、历史及 UDP 数据报传输。它单向依赖 `com.btsmtl`，核心包不依赖本包。Fantasy、Quantum3 和自定义网络分别由自己的适配层接入核心，不要求经过本模型或数据报实现。

Unity 项目需要本模型时，在 manifest 中显式安装本包及本地核心包；程序集仍为 `ThirdPersonSimulation.ServerAuthoritative` 和 `ThirdPersonSimulation.ServerAuthoritative.Transport`，源码和 meta 只保留本包一份。

普通 .NET 从仓库的 `Projects/ThirdPersonSimulation.ServerAuthoritative.Transport/ThirdPersonSimulation.ServerAuthoritative.Transport.csproj` 构建本包及完整依赖；核心解决方案 `BTSMTL.sln` 不包含网络包工程。

宿主提供原控制 transport、来源端口、执行工厂与 Session 装配。票据、房间、地址、握手、确认、重传、队列和模型策略归本包或宿主，不进入核心。

外部后端通过核心的运行端口、输入、Pipeline 执行与输出、状态捕获和恢复合同接入。输入 Span 和已编码状态字节是只读借用，使用期间保留原输入／状态 owner；准备输入视图不接管源数据，退出使用期归还对应视图。Pipeline 状态跨调用保留时配对调用 RetainStepStorage／ReleaseStepStorage。预分配状态解码由核心读取入口持有内部页，后端不接触内部 Bank 或 Page。

本次拆分保持原模型行为、协议与容量；包拆分和编译通过不证明网络、回滚或内存性能已验证。
