# 可选内存快照

`Float32MemorySnapshotProvider` 和 `FixedMemorySnapshotProvider` 位于 `ThirdPersonSimulation.Snapshots`。宿主按需把提供者交给原 Session composer。核心只声明提供者接口、正式状态与恢复协议，不引用此包，也不要求单机安装全量快照提供者；普通 Tick 事务和 Abort 继续使用原事务页及输入、战斗的内存载体。

提供者在 `PrepareCheckpointStorage` 中准备槽位。捕获保留已提交的 `SimulationWorldStateSet` 及原 Pipeline participant 状态，不复制角色字段表，不创建另一份字节数据，不编码、不解码、不遍历状态计算哈希。角色和世界的资源遍历由原成员上的 `ValueOperations`、`ValueResource` 生成。输入和战斗的独立载体由原成员上的 `ValueSnapshot` 生成。

| 入口 | 租约归属 |
| --- | --- |
| `CaptureStepWorldSnapshot` / `CaptureStepSnapshot` | 返回一次持有，由步骤输出的原释放边界归还。 |
| `CaptureCheckpoint` | 返回一次持有，由 `SimulationSessionCheckpoint.Dispose` 归还。 |
| `SimulationSessionCheckpoint.State` / `ReadCheckpoint` | 借用既有状态；独立延长持有期时须先 `Retain`。 |
| `ReadWorldSnapshot` | 为恢复调用方增加一次状态页租约；由原恢复事务归还，不消耗快照。 |
| `BindState` | 把传入的一次持有交给 checkpoint。复用前先释放上一份 checkpoint。 |

多个快照持有同一提供者槽时只增加槽计数，最后一次释放才归还其原状态页。提供者不释放 solver 的借用 `Capture` 结果。`DisposeCheckpointStorage` 解除提供者对准备槽的引用；消费者仍负责归还自己持有的步骤或 checkpoint。

容量来自现有 Session 的候选步骤、保留步骤、`RetainedCheckpoints`、`CaptureStates`、`RestoreStates`。纯内存历史会保留原角色、GE、世界及 participant 页，这些持有者必须进入原页池预算；槽位耗尽明确失败，不在捕获时扩容。初始化分配槽及数组，捕获与恢复复用原准备存储；运行分配、CPU 与恢复效果须在宿主实际场景检查，编译成功不能代替这些结果。

外部实现可以直接实现 `IFloat32SimulationSessionSnapshotProvider` 或 `IFixedSimulationSessionSnapshotProvider`，不需要提供 `Write`、`Read` 或状态哈希。自定义 `IValueSnapshotSource` 的独立载体应实现 `IValueSnapshotCopy`，复用既有容量复制内容、正确持有资源，并提供内容 `Equals` 供原恢复事务比较。当前自动生成支持原值、正式值联合、一维与锯齿固定数组、预分配集合、保留 comparer 的字典和固定 roster 中的嵌套状态源；创建后容器容量和 roster 形状保持固定。

字节内容发布、归档与网络编码由独立 MemoryPack 适配或具体后端消费原数据，属于其它可选能力。此包不依赖 MemoryPack 或网络包。

独立构建入口为 `Projects/ThirdPersonSimulation.Snapshot/ThirdPersonSimulation.Snapshot.csproj`，使用项目要求的 `--disable-build-servers /nr:false /p:UseSharedCompilation=false`，结束后执行 `dotnet build-server shutdown`。核心默认构建不安装或引用此包。
