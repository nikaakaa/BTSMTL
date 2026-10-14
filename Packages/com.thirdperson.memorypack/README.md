# 按需 MemoryPack 接入

核心状态和图类型只声明中立的成员用途，不引用 MemoryPack。本包从这些原成员生成外部 native formatter，内容发布使用宿主已经提供的 `MemoryPack.Core.dll`。Fantasy 及其它网络的同步策略归各后端。

`MemoryPackGraphContentPublisher` 供正式图构建入口使用。它保留结构、参数、来源三块：联合分支种类属于结构，活动分支数据和 `ValuePayload` 成员属于参数；参数或来源更新复用原结构块。`ReadTables` 用同一生成结果合并结构与参数，没有另一份业务字段表。

Float32 图产物的 `WriteGraph`、`ReadGraph` 和身份校验保留在同一 publisher 的领域 partial 中；`MemoryPackGameplayAbilityContent` 发布和读取两数值域的技能产物。`MemoryPackTimelineContent` 直接从原时间区间计算内容版本。这些入口只装配原内容对象，字段读写由外部 formatter 统一生成。

公开数据通过唯一原构造的参数与原成员自动匹配；原构造负责重建查询索引。同程序集的 internal 原构造也按实际可访问性处理。参数名与原成员不一致、私有原数据无法访问或构造有歧义时，生成器给出 `MPADAPT001`，不猜测业务映射。

`ValueStorage` 的原 owner 自动提供 `ReadValueStorage` 和 `FromValueStorage`，让外部适配消费 private 原成员。`UseConstructor=true` 只读取原构造输入，读取时调用该构造重建派生索引；私有只读集合转换为构造所需数组会产生冷路径分配。`ValuePresence` 保留原数据的无值语义，避免把空值送入要求有效身份的构造。

`ValueText` 表达借用文本：写入读取原文本 Span，不保存文本 owner 或页位置。冷加载调用原 string 工厂；准备读取时复制到调用方提供的原 `CanonicalTextReferences`。`System.Type` 交给宿主 MemoryPack 原生 formatter，保留枚举类型身份。

`MemoryPackContent` 是内容发布和加载的冷路径入口，写入创建缓冲并返回 `byte[]`，文本读取创建 string。纯内存快照不经过它；此入口不提供每 Tick 的 0 GC 保证。

已准备的输入读取使用 `MemoryPackContent.DeserializeInto(bytes, storage, context)` 两域重载。调用方在初始化时创建原输入 storage、文本引用存储、已发布身份目录和 `MemoryPackContentContext`；context 只保存一次创建的 options 和 UTF-8 解码字符缓冲。每次接收复用这些对象，官方 serializer 通过 `Options.ServiceProvider` 把同一 context 交给生成的 formatter。

生成器根据该正式重载的原返回类型与 storage 类型，按原输入成员顺序生成 `ReadInto`：调用原 `Begin`，逐元素读取并交给原 `AddValue`／`AddRequest`，最后按原 `CompleteRead` 参数完成输入。集合没有中间数组；原 owner 在完成边界执行既有顺序检查一次，不排序。返回的输入继续服从原 storage 的复用和释放生命周期。网络发送、接收排队、历史保留和可靠性策略仍由后端负责。

动态文本从 native 字符跨度写入原文本存储；原 `string` 必须在声明上标记 `ValueTextRules(Declared=true)`，读取返回身份目录内已经发布的同一个 string。当前原 `NumericProfileId.Value` 使用此分类。准备读取闭包出现未分类 string、没有原准备合同的嵌套集合或引用对象时，生成器报错，不退回分配路径。字符缓冲、文本池或输入容量不足沿原边界失败，不扩容。

`SerializeInto(in value, destination, context)` 使用同一缓存 options，把 native 数据写入调用方的 `IBufferWriter<byte>`。destination 的预备容量和复用由其 owner 负责。上述路径已生成以复用原存储的代码；实际运行分配、吞吐和 Unity Mono／IL2CPP 行为仍需在对应后端场景测量，静态生成与编译不等于实测 0 GC。

独立构建使用 `Projects/ThirdPersonSimulation.MemoryPack/ThirdPersonSimulation.MemoryPack.csproj`，显式传入宿主已有 DLL 的 `MemoryPackCorePath`。包内 `Analyzers` 提供对应工具产物及 RoslynAnalyzer 标记；更新工具时通过 `InstallUnityAnalyzer=true` 目标同步安装，普通编译不执行安装。

新增消费者程序集中的实际 `MemoryPackContent.Serialize/Deserialize` 泛型调用会驱动该程序集的 formatter 生成。该程序集在正式初始化边界调用其生成的 `MemoryPackGeneratedFormatters.Register()` 一次；本包已在自己的内容入口初始化。不要手写业务 formatter 或重复维护类型／字段清单。
