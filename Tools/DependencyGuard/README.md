# 依赖守护

`DependencyGuard` 是普通 .NET 工具，只依赖 .NET 和所用 SDK 自带的 Roslyn，目标框架与 SDK 主版本一致，不加载 Unity 或 BTSMTL 运行程序集。正式 `dotnet build BTSMTL.sln` 通过仓库根目录 `Directory.Solution.targets` 在解决方案 Build 前执行守护；失败会停止构建。Restore 不重复执行源码守护。解决方案目标导入采用 [MSBuild 正式机制](https://learn.microsoft.com/en-us/visualstudio/msbuild/customize-solution-build)。

## 输入范围

默认构建只检查 SDK 自身，不要求同级 3C，也不表示 Quantum 宿主检查已通过：

```powershell
try {
    dotnet build BTSMTL.sln --disable-build-servers /nr:false /p:UseSharedCompilation=false
} finally {
    dotnet build-server shutdown
}
```

完整工作区明确提供实际 Unity Client 根目录，另外检查该 Client 的 Quantum.Simulation、真实 GUID 归属与角色／技能作者身份。版本 7 要求该 Client 的本批正式编译证据，默认位置为 `Library/DependencyGuard/unity-boundary.json`。工具读取完整实际 rsp、源码、生成回执与当前程序集元数据，不启动 Unity，不删减实际编译输入；只有扫描完成且报告 `QuantumChecked=true`，才表示本次确实执行了 Quantum 规则：

```powershell
try {
    dotnet build BTSMTL.sln --disable-build-servers /nr:false /p:UseSharedCompilation=false /p:DependencyGuardClientRoot="D:/Unity_Project_1/3C/3cDemo/Client/3C_Client"
} finally {
    dotnet build-server shutdown
}
```

也可从同一正式解决方案入口只执行守护，不编译业务程序集：

```powershell
try {
    dotnet msbuild BTSMTL.sln /t:DependencyGuard /nr:false /p:UseSharedCompilation=false /p:DependencyGuardClientRoot="D:/Unity_Project_1/3C/3cDemo/Client/3C_Client" /p:DependencyGuardReport="D:/evidence/dependency-guard.json"
} finally {
    dotnet build-server shutdown
}
```

`DependencyGuardReport` 的父目录由调用方建立。报告记录规则版本、输入范围、是否检查 Quantum、规则编号、文件与行号、依赖链、作者身份来源、实际读取字节的 SHA256 与扫描结束时 SHA256。`VerifiedVendorSources` 记录本轮实际匹配的厂商来源、版本、原包 SHA、源码 SHA 与唯一根 asmdef；`ProjectBinaryReferences` 记录受插件规则约束的实际 csproj DLL 路径、同名 asmdef 归属及引用分类。`ActualCompilations` 记录本批声明、项目、rsp、产物和实际回执的对应；`AutomaticCompilationCandidates` 逐项记录自动来源、项目声明、源码使用、产物闭包使用与是否仅作输入；`VerifiedOfficialMetadata` 记录允许的具体源码范围、类型、官方来源、版本及生成回执。`InputsChanged` 表示扫描期间发生源文件变化；这是本次时点检查，不能据此宣称其它窗口的工作区已冻结。退出码 0 为本次范围无违规且已读取输入字节稳定，1 为架构违规；已枚举输入被删除、字节改变或扫描未完成时退出码为 2，无论是否输出 JSON 报告。证据缺失、实际执行时 SHA 不匹配或绑定不完整另报告 `ACTUAL_DEPENDENCY_EVIDENCE`、`ScanCompleted=false`，不将部分结果或旧产物当作通过。

报告另外保存实际执行的 `ToolBinaryPath`／`ToolBinarySHA256`，该二进制也参与结束时的输入复核。源码交接 SHA、正式构建回执和实际二进制身份必须对应，不能用同一规则版本的旧工具结果冒充修正后的实现。

## 规则与解析

`rules.json` 是唯一规则表，当前规则协议使用版本 7；工具只接受该版本，旧工具与旧规则不能忽略新证据协议或退役范围后继续报告通过。它覆盖全部 BTSMTL 程序集与工具、核心合同/运行时、领域合同、Shared 和 Quantum.Simulation；按真实 asmdef 名称解析 GUID 引用，按项目路径解析 ProjectReference 与本地 imports，按 package.json 验证依赖方向与包环。合同的允许引用表约束直接依赖；禁止的实现依赖沿完整依赖链和本批产物 AssemblyReference 闭包检查。构建期 analyzer 不作为运行程序集引用，仍完整保存其实际编译输入身份。

`unityPluginRules` 检查正式 asmdef 的 Unity 插件边界。BTSMTL、Shared 包和领域合同必须显式声明 `overrideReferences=true`；省略或 false 会接受 Unity 自动引用的启用插件，即使源码没有使用插件类型也不符合中立边界。预编译引用默认必须为空，只有已核对直接 DLL 需求的 `ThirdPerson.Shared.Contracts` 与 `BTSMTL.EventGraphs` 允许 `System.Runtime.CompilerServices.Unsafe.dll`。正常 asmdef 引用、Unity 引擎／基础库和构建期 analyzer 沿原职责解析，不作为预编译插件白名单。新增实际插件需求须先核对生产者和正式程序集引用，再修改同一规则表。

该规则同时检查正式声明及其唯一同名 csproj 的实际引用。Client 根目录的生成 csproj 继承真实 asmdef 包归属；受控项目缺少同名声明时报告 `UNRESOLVED_PROJECT_ASMDEF`，同名声明有歧义时报告 `AMBIGUOUS_PROJECT_ASMDEF`，不猜一个包或忽略旧生成项目。没有 Unity asmdef 的独立 .NET 工具继续受原依赖规则约束，不套用 Unity 插件声明语义。实际 HintPath 读取 DLL 元数据以确认程序集身份，另外区分安装中的 BCL／引擎库、具有唯一同名 asmdef 的 `Library/ScriptAssemblies` 产物及预编译插件。Unity 安装需有实际 `Editor/Unity.exe`；其 `NetStandard`、`UnityReferenceAssemblies`、Mono 参考库按 Framework 分类，`Managed` 原生引擎库与 `PlaybackEngines` 内正式 `UnityEditor.*.Extensions` 按各自 SDK 职责分类。分类同时核对文件名与实际程序集身份。SDK、Client 和本地链接包内部的同名 DLL 仍是插件，不因目录片段或 `System`／`Microsoft`／`UnityEditor` 名称获得基础库身份。

`Microsoft.Win32.Primitives` 等真实参考库 shim 通过同一 `BinaryReferenceKind` 的 Framework 来源满足合同基础库允许条件；明确禁止的程序集／包规则仍先适用，不新增 Microsoft 名称白名单。插件继续应用同一直接插件允许表，因此 Performance、Luban、BlackboardConfiguration 等中立程序集的生成项目也受约束。`PlaybackEngines` 的 SDK 分类不自动扩大 Quantum 原厂必要直接引擎例外。asmdef 或 PluginImporter 已修改不代表生成工程边已失效，独立编译按声明过滤引用也不能代替 Unity 正式再生 csproj／Bee 的实际引用证据。来自 PackageCache 的 nunit 仍是预编译插件；是否只是自动候选依据本批完整证据判断，不按名称或 Editor 身份放行。

源码按最近的 asmdef、asmref 或实际项目 Compile 归属，使用 Roslyn 读取声明、类型引用、别名、枚举成员与字符串。完整 Client 还读取正式 `Packages/manifest.json` 的本地 `file:` 包；外部链接包的声明和源码参与同一次 Catalog、归属及 SHA 输入，不要求它们物理位于 Client 或 SDK 目录。实际项目的 Compile（含 glob、Exclude／Remove）和 None 中的 asmdef／asmref 沿原项目解析器接收，待全部声明读取后唯一绑定项目身份。没有 Unity 声明的独立项目链接源码按真实编译项目检查；有明确声明的源码继续使用该声明。受控或 Quantum 依赖项目的缺失／未解析源码输入报告 `UNRESOLVED_PROJECT_SOURCE`，不静默跳过整个外部依赖。Unity 不导入的 `~` 目录、构建输出和版本管理目录不作为目录枚举输入。

成员接收者由同一实际程序集的源码进行 Roslyn 词法符号绑定，包含跨文件 partial 成员、局部变量、参数、字段、属性、事件和查询变量。已绑定的值接收者不再猜成同名作者类型；真实类型位置、静态类型引用、命名空间及独立的退役类型／字符串／身份检查继续适用。完整 Client 另以实际 rsp 的 ParseOptions、完整引用及本批生成源建立真实语义绑定，包含继承而来的 Unity 成员。`IOperation` 的完整子操作遍历将实际用户运算符、隐式／显式转换、复合赋值及参数出入转换的方法符号交给同一 `SourceBindings` 接收路径，记录实际使用程序集并应用原依赖规则；不从操作符文本或方法名猜来源。普通名称和这些操作共用官方元数据的具体范围核对，允许的别名或属性不会放行实际调用。

实际 rsp 的全部源码和 `GeneratorOutputs` 语法树同时进入唯一 `sourceFiles` 集合与真实编译 asmdef 归属，随后同一结构扫描器检查退役类型、指令、角色／技能及 Timeline 结构。生成源即使位于 `obj`、提取目录或原目录枚举之外也不会漏扫；同一文件确实参与多个程序集时按各自 owner 检查。厂商源码许可仍绑定该文件及原根 asmdef，不随同一物理文件传播到另一个程序集。已经按正式回执接收的源码与归属保留供结构扫描；后续语义或政策证据失败会撤销候选及元数据许可，并把实际阶段标为未完成，不把结构扫描当成语义成功。工具不生成或执行被检查程序集。结构规则继续扫描全部条件分支；官方元数据允许范围由本批实际编译的具体源码范围确定，注释不作为代码。角色规则保留已知项目角色；完整工作区另外从 `Assets/Configs/Character` 的角色目录和资产 `m_AbilityId` 收集实际作者身份。技能名只在技能身份常量或按该身份比较/分派的位置检查，普通同名端口不等同于技能。

唯一核心指令枚举为 `BTSMTL.Orchestration.OrchestrationOperationCode`，声明见 [OrchestrationOperationCode.cs](../../Packages/com.btsmtl/Runtime/Orchestration/Contracts/Program/OrchestrationOperationCode.cs)。旧 `ThirdPersonSimulation.SimulationOperationCode` 已退役。扫描器对正式枚举的声明、成员访问、别名及 `using static` 检查规则表中的 43 个已删除领域指令；领域能力通过正式 Extension 合同接入，不重新加入核心枚举。

退役类型继续由唯一 `retiredTypes`／`RETIRED_TYPE` 路径检查。版本 6 原样保留版本 5 的全部 920 项：原 893 项、按 `quantum-gameplay-hosting` 4.5 加入的 25 个 Canonical 类型身份，以及按 `btsmtl-generic-orchestration-core` 5.1 加入的 `ThirdPersonSimulation.IValueTextCapacity` 与 `ThirdPersonSimulation.ValueTextCapacityAttribute`。包含实际嵌套类型和泛型元数。来源是 Shared 的 `Serialization/CanonicalData.cs`、`CanonicalBuffer.cs`、`CanonicalDeclaredIdentityReader.cs`、`SimulationCanonicalPayloadHash.cs` 及文本容量合同；已移除的 `CanonicalInitializationTextResolver` 与 `ValueUnionContract.cs` 内 `CanonicalUnavailable` 依据保存的真实源快照登记。完整类型身份位于同一规则表，不按名称包含 `Canonical` 判定无关类型。

Catalog 已接收且有实际源码 owner 的每个程序集都执行退役类型检查，新包或不在受控前缀内的程序集不能继续声明或引用 `ThirdPersonSimulation.CanonicalTextValue`。依赖、角色／技能及 Timeline 规则仍使用各自正式适用范围。类型解析优先使用实际词法绑定的完整符号身份，未解析的外部类型沿原命名空间、别名、嵌套类型与泛型元数解析；属性短名按正式 `Attribute` 身份处理，声明、模式匹配与 `is/as` 等真实类型位置同样接入。字符串／反射检查仍针对规则表中的完整类型身份，不新增 Canonical 关键词禁令或旧文件豁免。

迁入 Shared、Movement、独立领域合同及中立作者合同且保留全名的唯一正式类型不属于退役类型；判定依据是实际声明及其归属，不能把迁源当作删除。通用字符串归一、`ValueOperations`、`ValueTextRules` 等生成能力不因名称相关而退役。新增退役类型时更新同一清单；当前违规由对应模块改正。

`timelineBoundaryRules` 约束 BTSMTL 的正式 Timeline 扩展面。扫描器解析完整类型名、别名、partial 和传递继承关系：Track、Clip、内容参数只允许规则表列出的通用基类、Tree 与内置场景参数实现，领域派生类必须属于领域程序集。样本按正式 `TimelineRuntimeSampleView<T>`／`TimelineRuntimeSampleBuffer<T>` 的元素类型检查，只允许通用采样记录、Tree 请求和播放事件；不按相机、动画、角色或技能名称推测业务。合同种类检查内置种类常量表以及 Track／Clip 合同构造的可解析字符串常量，包含别名、跨文件常量和常量拼接；由调用者传入种类的通用构造入口保留开放职责。领域包中的同名类型和其自身种类不受 BTSMTL 声明规则限制。

这些允许项描述引擎拥有的协议原语，不是领域或内容白名单。新增领域轨道不改规则；新增 BTSMTL 原语必须同步其架构合同和规则。移出尚未完成的旧领域声明仍应失败，不设过渡豁免。

## Quantum 厂商与项目边界

项目模拟源码及其实际项目依赖禁止使用 Unity 与表现业务 API；已核实的原版 Quantum／KCC 源码保留厂商必要的接入。`vendorSourceProvenance` 按原 UnityPackage 的来源、版本、包 SHA256、Client 相对路径、源码 SHA256、根 asmdef 和特定规则编号声明来源；扫描时只在实际源码归属于该根 asmdef、路径及当前字节均匹配时，排除这一文件的 `QUANTUM_NO_UNITY_PRESENTATION` 规则。原版 Quantum 3.0.14-Stable-2241 有 3 份 Simulation 源码，KCC 3.0.6 有 31 份；原包 SHA256 分别为 `5E12AFA93ACACE8F99A8BD7A396DF3A3EA4F02E7177B52D2D7D3C5F2C5D5146C`、`71B15E22A9633FD54EEB6E044AFD9399B2E8934CC957281AC4EC346C16924C3D`。原包逐字节核对证据随交接保存，版本 5 保持版本 4 的该范围原样。

新增、修改或移到其它程序集的文件自动适用项目规则；来源目录本身没有豁免。退休类型、角色／技能身份与其它适用规则继续扫描这 34 份源码。原厂声明例外仍仅允许同一已核实根模块的直接基础引擎引用：asmdef 的隐含 `UnityEngine`，以及实际 csproj 指向 Unity 安装 `Editor/Data/Managed` 的 `UnityEngine`／`UnityEditor` 基础库。版本 4 另外核实必要官方生成元数据及 `Quantum.AssetObject` 的资产继承用法：生产者、版本文件和运行程序集 SHA 固定在同一规则表，生成源须来自本批成功生成回执；只允许列出的具体别名、属性名或基类用法及其必要产物依赖。引擎类型须实际绑定本批安装目录中的原件。项目调用 Unity API、使用 `FormerlySerializedAs`／`MovedFrom` 兼容属性、引用表现或把新增代码登记为厂商源仍失败，不能豁免整个文件、程序集或依赖闭包。

`checkProjectDependencySources` 沿实际 asmdef／ProjectReference 关系收集 Quantum 的依赖源码，即使某依赖名称不属于原受控前缀，也应用同一项目模拟边界。厂商预编译 SDK 在真实 DLL 边界结束源码遍历；不会因其名称或所在目录放过其它项目源码。SDK-only 不包含 Quantum 宿主，其成功不能代替完整 Client 检查。

## 本批实际编译证据

`unityActualBoundary` 是同一规则表中的正式证据入口，不新增另一套守护。完整 Client 默认读取 `Library/DependencyGuard/unity-boundary.json`；直接运行同一工具时可用 `--unity-evidence <绝对路径>` 指定同一协议的文件。SDK-only 不读取该文件。共享编译、QTN 与证据采集仍由 q3 唯一队列执行，守护只读其真实产物。

外层对象的字段为 `Version=2`、`RulesSHA256`、`BatchId`、`ClientRoot`、`Files`、`Compilations`、`Installations`、`Generations`，均为必需字段。`RulesSHA256` 必须是本次执行工具读取的版本 7 规则字节身份。外层形状未改变，但其引用的原始政策快照必须使用下述 Version2；旧 Version1 证据、旧政策快照和旧工具不能冒充本版本，旧 `AutomaticReferences` 数组入口已删除。所有路径使用真实绝对路径，所有文件先按同批快照 SHA 接收，并在扫描结束再次比较当前字节。文件清单必须覆盖守护实际读取的声明、源码、作者身份、链接包、rsp／嵌套 rsp、编译器及生成器输入、回执、原始政策快照、参考程序集、当前输出与完整实际传递依赖；工具自身、规则文件与外层证据文件由守护独立记录身份。

- `Files` 每项为 `{ Path, SHA256, Kind }`。`Kind="assembly"` 只登记当前实际产物和完整运行依赖闭包的 PE 原件，同一完整程序集身份只能对应同一字节；编译参考程序集同样完整捕获，但使用 `Kind="compiler-reference"`，不能拿不同字节的 ref DLL 替换实现 DLL 的闭包。其它种类按真实输入职责记录。
- `Compilations` 每项为 `{ AssemblyDefinition, Project, ResponseFile, WorkingDirectory, Output, Receipt, CompilerTrace, GeneratorOutputs }`。`Project` 是 Client 根目录中对应唯一 asmdef 的真实 Unity 生成工程。守护按完整 rsp 解析所有源和元数据引用、别名、编译选项、analyzer 及附加输入；不得删掉 UI、测试、平台或其它候选引用。`GeneratorOutputs` 保存同一成功编译调用实际生成的源码，不能重跑另一生成器拼接。全部受控及项目模拟依赖的 Unity 工程均须覆盖，缺少一个就检查未完成。
- `Receipt` 指向 `{ BatchId, ExitCode, Inputs, Outputs }`。`Inputs`／`Outputs` 为“实际绝对路径 → 执行当时 SHA256”的字典，`ExitCode` 必须为 0，批次须一致。输入同时涵盖该次准备／编译所依据的正式 asmdef、生成工程、rsp、源、参考、生产者和版本文件；输出涵盖当前 DLL、实际生成源和 `CompilerTrace`。仅列路径、仅记 saved 或事后给旧编译补当前 SHA 不符合协议。
- `CompilerTrace` 指向 `{ BatchId, OriginId, AssemblyDefinition, ResponseFile, Sources, References, ReferencePolicyFacts, PolicyCaptureSource, AutomaticReferencePolicies }`。完整源与引用集合须与原始 Bee 和实际 rsp 一致，不删 UI、NUnit 或其它候选。`ReferencePolicyFacts` 指向本批原始官方对象的政策快照，`PolicyCaptureSource` 是实际调用的下述采集源码，`AutomaticReferencePolicies` 每项为 `{ Reference, Kind }`，只提出有真实快照支持的来源主张。它不是官方原方法的插入 delta；Unity 当前安装没有提供那份数组。旧字段被协议明确拒绝。
- `Installations` 每项为 `{ OriginId, Producer, VersionFile, RuntimeAssembly }`，实际文件须匹配 `officialProducers` 的版本与 SHA。当前核定来源为 Unity 2022.3.62f2c1 安装编译输入生产者，以及 Quantum 3.0.14.2241／QTN 33 生成器和 `Quantum.Engine.dll`。SDK 升级时先重新核实同一职责与真实生产过程，再更新同一规则表。
- `Generations` 每项为 `{ OriginId, Receipt }`；实际 QTN 回执使用上述含执行时 SHA 的协议，输入须包含核定生成器、版本文件及真实生成输入，输出记录实际生成源。项目源不得冒充官方生成输出，官方生成文件中的业务代码也不因同文件有允许的元数据而获豁免。

自动候选只在以下事实同时成立时记为 `InputOnly=true`：同批原始官方政策事实及 rsp 证明自动来源；所属 asmdef 或包及真正作者响应文件／AdditionalCompilerArguments 未主动声明；真实源码绑定未使用；当前产物及完整 PE 闭包未引用。项目主动声明即使未使用仍按禁止规则失败。生成 csproj 的自动边可以据完整证据判定，独立 .NET 工程的显式声明不能套用这个规则。绑定、产物、闭包或执行时身份缺失时明确失败，不从旧缓存补齐、不删除输入、不建立固定候选名称豁免。

## 原始自动政策事实采集

安装原件的 `EditorCompilation.CompileScriptsWithSettings` 在引用合并和 `BeeDriver.BuildAsync` 后保存 `_currentBeeScriptCompilationState`，随后才触发 `compilationStarted`。Bee `AssemblyData` 仅保存 `PrebuiltReferences` 和 `References`，没有自动来源字段。公开 `CompilationPipeline.GetAssemblies()` 会重新运行 `GetAllScriptAssemblies/ToScriptAssemblies`，不能用来补原调用记录。离线核实证据见 3C 协调目录的 `dependency-guard-automatic-production-capture-handoff.json`；v5 原交接及失败证据保留。

唯一原始事实读取方法为 [UnityReferencePolicyCapture.cs](UnityReferencePolicyCapture.cs) 的 `Capture(string batchId, Assembly editorCore, object compilationContext)`。该文件是独立 C#9 源，不引用 Unity；供 q3 在既有正式 `CompilationPipeline.compilationStarted` 采集回调中调用：

```csharp
UnityReferencePolicyCapture.Capture(batchId, typeof(UnityEditor.EditorApplication).Assembly, compilationContext)
```

方法不注册回调、不安装监控、不触发编译、不调用引用选择方法。它直接读取该回调对应的原 `ActiveBuild`、`Settings`、`ScriptAssemblies`、`customTargetAssemblies`、当前官方提供方缓存及原静态政策字段，并在同次回调调用官方 `UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()` 读取当前已注册选包结果。返回值必须在该次回调中立即由既有记录器序列化并保留 SHA，作为本次过程实际 Outputs；实际采用的采集源码须在 Inputs，字节 SHA 须匹配 `unityActualBoundary.policyCaptureSourceSHA256`。q3 在明确释放后以私有采集候选采用这份原源，不擅自写入冻结 Unity 工程。新增选包读取只核实了安装 DLL 中的真实公开方法和属性，尚未执行本版采集。

快照为 `UnityReferencePolicyFacts` 的 Version2，保留真实加载生产者的路径／MVID、项目与编译设置、原目标／脚本对象、预编译缓存的 Path／Flags／Redirected、原自动包集合、TestRunner 原常量与实际开关、原 CodeGen 政策常量，并新增必需的 `SelectedPackages` 数组。每项 `{ Name, Version, PackageId, Source, AssetPath, ResolvedPath }` 均直接来自上述官方已注册包对象；`Source` 是原枚举的文本表示。原缓存为 null 仍保留 null，不补空数组。目标的 `References` 已是合并值，工具不声称恢复插入前列表。原 `CompilerOptions.ResponseFiles/AdditionalCompilerArguments` 参与同一 rsp 解析器和声明判断，原文件及嵌套文件必须属于真实执行 Inputs。

Unity 原件中的 `Packages/<包ID>/...` 是正式虚拟包路径。接收边界先接收同批原始政策与 `SelectedPackages`，核对 `ResolvedPath/package.json` 的实际 name／version、正式虚拟根及执行时 SHA，然后用该唯一已选根解析本批政策、rsp、参考与嵌套响应文件；注册表、Git、本地、内置及嵌入包沿同一路径，不猜 `PackageCache` 目录或固定包名。每份已选包的真实 `package.json` 必须进入同批 `Files` 和编译流程 `Receipt.Inputs`；此前未读到的已选外部根由原声明／源码加载器纳入同一 Catalog 和输入 SHA。虚拟根没有对应的本批原始选包事实时明确未完成，不回退拼接 `Client/Packages`。独立声明阶段仍读取正式 manifest 的 `file:` 包，以便缺 Unity 证据时继续该阶段的真实结构检查。

`DependencyCatalog.ReferencePolicies.cs` 对提出的单项来源主张核对原事实，不运行 Unity 选择过程，也不由“最终参考减声明”制造自动集合。可核对的 Kind 为 `DefaultPrecompiled`、`DefaultEngine`、`AutoReferencedPackage`、`TestRunner`、`NUnit`；包／测试名称全部来自该次官方原字段，没有 UI／NUnit 名称允许表。预编译缓存只在实际 Editor 且无额外 defines 的原保存分支有效；重定向或没有原缓存的候选不能获证。平台模块的临时 `EditorAssemblyReferences` 没有保存在该快照中，此类别仍未完成，不能靠安装路径、事后 getter 或新插桩补成已证来源。没有主张的参考继续保留并应用完整依赖规则，不自动获得豁免。

## 扫描阶段完整性

同一正式入口依次接收 `DeclarationInputs`、`UnityEvidence`、`SourceOwnership`，然后运行独立的 `DeclaredDependencies` 和 `StructuralSourceScan`，最后核对 `ActualCompilationCoverage`、`ActualDependencies`、`ActualSourceBindings`。`UnityEvidence` 缺失或已知协议错误不会跳过已建立的 SDK＋显式 Client 声明、全部已归属源码、920 退役类型、43 指令、角色／技能及结构 Timeline 规则。失败的证据接收会丢弃部分加载的自动候选和官方元数据许可，不能拿半份输入放行后续规则。

独立声明阶段检查正式 asmdef／包和独立工程；Client 根目录生成 csproj 的自动边留给实际证据阶段，不将旧生成边伪装成当前主动依赖。词法结构扫描继续访问全部条件分支和真实 owner，但不代表已完成外部二进制的语义绑定。完整 Client 仍须完成实际 rsp、全部生成源、当前产物和完整 PE 闭包阶段，Unknown 不当作语义通过。

报告保留总体 `ScanCompleted/QuantumChecked`，新增 `Stages`、`DeclarationScanCompleted`、`SourceScanCompleted`、`ActualSourceBindingsCompleted` 和 `ScannedSources`。`SourceScanCompleted` 只说明已归属源的结构规则全部遍历且输入稳定；不能代替实际符号检查。必要阶段缺失或输入变化时总体 `ScanCompleted=false`、`QuantumChecked=false`、退出 2；所有必要阶段完成但有违规时退出 1；当前范围全部完成、稳定且零违规才退出 0。SDK-only 的实际 Unity 阶段标为 NotRequired，`QuantumChecked` 始终 false。明确的输入缺失、JSON／XML 协议及无效 PE 记录到对应未完成阶段；没有全包 catch 或备用扫描器。

## 当前验证边界

版本 2 的 `dependency-guard-current-01` 因并行删除已枚举源码而未完成，不是零违规结果。版本 3 的 current-02 与后续 current-04 均已完成完整稳定扫描并失败；最新 current-04 为 4069 条命中（4064 条 Quantum、5 条插件），不是独立问题数。其后用户明确批准上述实际依赖边界，版本 4 正式替换原候选判定及必要官方元数据边界。该批准不改变旧失败报告，也不能扣除已知误报数量后报告通过。

版本 4 的工具 build-02 已实际编译成功：13 项输入稳定、零警告／零错误、shutdown exit0；完整扫描尚未执行，该结果保留为 v4 源码证据。版本 5 新增精确 Canonical 退役与新包源码覆盖，其 13 输入和交接完整保留。版本 6 统一实现阶段完整性与原始政策事实协议，原 15 输入及交接保持历史不变；q3 的 v6 tool-build-02 已完成工具构建，不等于完整扫描成功。版本 7 补齐真实运算符／转换绑定、rsp 额外／生成源结构覆盖及官方已选包根事实；当前仅完成源码、安装 API 元数据和静态检查，未编译、未采集、未执行守护。构建和完整实际证据扫描仍归 q3 唯一队列，旧工具不能代表本版本。正式解决方案目标继续编译并调用同一 `DependencyGuard.csproj`，本轮未新增工程或扫描入口。QTN 项目生成源不登记为原包厂商源码，必要元数据按本批来源与具体用法核实。当前提交门槛仍是新规则、新工具以及当前完整稳定输入对应的零违规报告；工具编译成功或独立 SDK-only 通过不能代替它，不据本批标记 M0 完成。
