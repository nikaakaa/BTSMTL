---
name: btsmtl-preview
description: 接入或修改 BTSMTL 的 Unity 预览播放器项目适配、组件存档与检查点恢复时使用，说明通用代理与项目的职责，以及自定义状态恢复自检。普通作者内容编辑不触发。
---

# 接入 Unity 预览播放器

项目提供正式游戏内容、输入、时钟和观察数据。`BTSMTL.Preview` 提供连接、播放控制、帧画面采集与检查点选择。沿项目原执行器推进，不在预览代理中复制玩法、动画求解或输入日程。

源码入口为 [Preview 包](../../../Packages/com.btsmtl/Preview)，项目合同为 [IPreviewProjectAdapter](../../../Packages/com.btsmtl/Preview/Runtime/Agent/IPreviewProjectAdapter.cs)。先读取本次实际接入类型及项目的正式加载、提交、表现和释放调用链，再修改接线。

## 实现项目的五项职责

以普通 C# 类实现 `IPreviewProjectAdapter<TCommand>`。`TCommand` 继承 `PreviewAgentCommand`，只增加项目字段。项目的 MonoBehaviour 负责 Unity 生命周期与装配。

| 职责 | 项目实现与结果 |
| --- | --- |
| 载入运行数据 | `Load` 沿原资源和内容准备入口建立会话；只有正式准备完成才报告就绪。换技能沿同一会话装配合同，不额外启动执行器。 |
| 前进一帧 | `AdvanceFrame` 请求原时钟推进。依赖 Unity `Update` 的项目等待正常下一帧；手动时钟项目调用自己的正式单步。 |
| 注入输入 | `InjectInput` 写入原设备或输入日程。恢复后保留原日程起点和输入游标，不把恢复帧当成新输入起点。 |
| 报告每帧事实 | `ReportFrame` 返回真实已提交帧、内容身份和准备状态。同一提交边界把实际骨骼、物理、相机和逻辑结果交项目原观察生产者；此返回值不替代观察格式。 |
| 存档和读档 | `Checkpoints` 返回 `IPreviewCheckpointAdapter`，由项目组合后端、编排与表现状态，并声明实际恢复能力和限制。 |

接入 `PreviewAgentConnection` 的命令、回执及断开状态。命令的 `executionMode` 指定 `batch` 或 `interactive`，项目的相机 `mode` 和预览档位各保留自己的业务字段。将连接失败的 `Failure` 交正式错误回执，宿主断开后沿原生命周期结束播放器。停止时由唯一所有者等待 `CloseAsync`，释放会话和采集资源。

使用 `PreviewPlaybackController` 管理交互和批量状态。只有项目确认的新帧才能传给 `ObserveFrame`，命令接收和调用一次单步都不代表帧已提交。交互按实时推进；批量使用固定帧时长、不限帧率、不推流，任务结束后等待下一任务。Unity `Update` 项目不能用紧循环调用 `Update` 来代替引擎帧。手动 Animator、Animancer、Playables 和物理推进只能由原项目时钟统一持有。

画面在对应表现提交后的帧末交 `PreviewFrameCapture.TryCaptureAtFrameEnd`。构造时传入实际画面面板尺寸、编码和质量。运行中的画质命令在主线程写 `Quality` 属性，下一次 `TryTakeCompleted` 编码读取当前质量，无需重建采图器；合法取值由原命令接收边界确认一次。批量任务在采集忙或上次画面尚未取出时保留当前帧，不能跳过该帧后继续标记成功。在 Unity 主线程调用 `TryTakeCompleted`，它使用 `EncodeNativeArrayToJPG` 或 `EncodeNativeArrayToPNG` 编码，返回的 `PreviewEncodedFrame.Bytes` 是本次结果独占的持久 `NativeArray<byte>`。消费者写完或复制完后调用一次 `Dispose`，不把它转为逐帧新建的托管数组。

逐帧 JPEG 及来源头、索引交项目同一运行档案写入器，读取方沿同一档案 reader 取画面，不另建 JPEG 目录与路径协议。`PreviewPngImageArchive.Write` 只用于明确的独立 PNG 截图。运行清单和通道布局仍由项目归档器持有。采集或编码失败按 `Failure` 报错，不生成占位图片。原生编码的主线程和持久内存约束见 [Unity 2022.3 JPG 接口](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/ImageConversion.EncodeNativeArrayToJPG.html)。

## 找出必须存档的状态

从项目的“提交本帧”追到“计算下一帧”，列出下一帧会读取的可变状态及其唯一所有者。重点读取移动速度与接触历史、技能和效果计时、命令队列、输入游标、随机源、动画过渡、相机平滑历史、生成对象及稳定身份映射。配置和不可变内容保存身份，不复制成另一份可写配置。

后端已有正式快照时复用它。Quantum 保存 SDK 整帧，不在适配器逐字段重写 Frame。图宿主继续使用 `HostedGraphRuntime` 的 `PrepareRuntimeObservation`、`GetRecordedStateCapacity`、`WriteRecordedFrameState` 和 `RestoreRecordedFrameState`；录制正文位于 `HostedGraphRuntime.Recording.cs`，与 Editor 使用同一格式和事务。表现历史不因来源是 Gameplay 就省略，只有原正式重建入口能从该快照完整重建的结果才由重建入口恢复。

组件和自定义状态实现 `IPreviewStateAdapter`：

1. 用 `Identity` 标识稳定对象与状态，用 `Capacity` 一次声明字节和文本引用容量。
2. `Write<TWriter>` 按固定顺序写入正式 `IRecordedRuntimeStateWriter`，`Restore` 按相同顺序读 `RecordedRuntimeStateReader`。
3. 准备阶段把适配器装入 `PreviewStateLayout`，每个检查点槽预建一份 `PreviewStateSnapshot`。提交帧调用 `Capture`，恢复时调用同一快照的 `Restore`。
4. 选择真实的 `RestoreStage`。布局按 Animation、Transform、Physics、Project、Random 恢复，最后统一 `Physics.SyncTransforms`。

固定数组和大状态留在预分配存储，逐帧直接访问已有元素，不把整块状态放进按值参数或临时栈。对象生成、删除或组件布局改变时，由项目重建对应布局和检查点；不能把旧槽套到新布局。文本引用只适用于本进程内存快照，需要落盘时沿所属正式文件格式保存其内容。

## 按组件能力选择适配器

默认实现位于 [State](../../../Packages/com.btsmtl/Preview/Runtime/State)，全部要求项目用实际场景自检。

| 实现 | 保存内容和限制 |
| --- | --- |
| `TransformPreviewState` | 局部位置、旋转、缩放和 `activeSelf`。对象层级保持不变；启停回调产生的业务状态由项目保存。 |
| `AnimatorPreviewState` | 各层状态、时间、权重、过渡及 Float、Int、Bool 参数。Trigger 没有公开读取 API，存在 Trigger 时声明 `RestartOnly`，由项目自己的触发状态适配解决。动画事件和状态回调仍需自检。 |
| `RigidbodyPreviewState` | 位姿、线速度、角速度、运动学与碰撞开关、休眠状态。PhysX 原生接触缓存未包含，不能由字段恢复推定下一帧相同。 |
| `CharacterControllerPreviewState` | 位姿和启用状态。原生速度与接触没有公开恢复入口，默认声明 `RestartOnly`；项目需使用移动模块实际持有的状态及正式恢复入口。 |
| `RandomPreviewState` | `UnityEngine.Random.state`，最后恢复。项目的其它随机源由各自所有者保存。 |
| `AnimancerPreviewState` | 可选 `BTSMTL.Preview.Animancer` 程序集，安装 Animancer 8 才启用。保存已准备的固定层和 ClipState 的时间、速度、权重与播放状态；活动淡入淡出、Mixer、Controller 和自定义状态需要项目适配。 |

基础程序集不引用 Animancer。不要为不支持的原生状态补零、推算速度或静默跳过。`PreviewStateLayout.CreateSnapshot` 拒绝 `RestartOnly` 布局；项目先补齐缺失的正式状态入口，再建立可恢复布局。

## 自检恢复并开放续跑

恢复能力有三档：`RestartOnly` 只能重跑，`SelfCheckRequired` 已实现但尚待自检，`Verified` 已在声明条件下通过自检。默认实现不会自行升级为 `Verified`。

自检直接调用项目 `IPreviewCheckpointAdapter` 的存读档入口，检查尚未获准续跑的实现。不要经只接受 `Verified` 的 `PreviewCheckpointStore.RestoreAtOrBefore` 伪造通过结果。

1. 固定内容、代码版本、对象布局、原输入日程、时钟、种子和比较容差。
2. 在实际已提交的第 N 帧调用 `Capture(slot, N)`，沿原入口运行到 N+30，保存第一份实际观察。
3. 调用 `Restore(slot)`，保留原输入位置，再沿相同入口运行到 N+30，保存第二份观察。使用 Unity 生命周期的项目仍等待真实引擎帧。
4. 按实际帧和稳定对象身份比较两份骨骼、物理与相机结果。确定性字段精确比较，其它字段使用预先声明的容差。缺失通道报告不可用，不补默认值。
5. 报告首个差异帧、对象、骨骼或组件、字段、两次实际值与容差，再沿该字段的生产和恢复链找漏存状态。

只有实际比较通过，项目才能在该内容、布局和代码条件下声明 `Verified`。失败或尚未执行时保留原因和限制，续跑请求沿正式入口从头重跑，档案回看继续直接读原记录。没有通用自检命令时由项目把上述过程接入自己的正式入口，不在文档中虚构 CLI 或成功回执。

项目创建 `PreviewCheckpointStore`，在正式提交帧调用 `CaptureCommittedFrame`，Timeline 关键位置传 `keyPosition=true`。续跑请求调用 `RestoreAtOrBefore`；只有 `Restored` 的返回帧能交播放控制器继续快进。`RestartOnly`、`SelfCheckRequired`、`NoCheckpoint`、`ContentChanged` 交宿主明确说明并从头运行，不能标记为已经恢复。内容或布局变更沿项目正式兼容规则处理，不放宽后端身份校验。

## 构建和采用

预览播放器使用 Mono，独立构建关闭 Burst AOT。会替换的业务逻辑放普通 C# 类，场景组件只作固定装配、生命周期和表现桥接；DLL 更新不能改变场景及预加载资产所用脚本类型和序列化字段。

沿项目正式正常 Editor 构建入口完成导入和 Build，再由宿主采用实际成品。独立源码编译、Editor 编译、成品采用、恢复自检和实际运行分别报告。编译器临时 DLL 不直接复制进播放器，已有旧构建成功也不证明新增适配已生效。图像编码与网络 I/O 的分配和性能按实际测量说明，不把状态缓冲预分配等同于整条链零 GC。
