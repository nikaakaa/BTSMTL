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

完整工作区明确提供实际 Unity Client 根目录，另外检查该 Client 的 Quantum.Simulation、真实 GUID 归属与角色／技能作者身份。工具只读取源码和依赖内容，不启动 Unity；只有扫描完成且报告 `QuantumChecked=true`，才表示本次确实执行了 Quantum 规则：

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

`DependencyGuardReport` 的父目录由调用方建立。报告记录输入范围、是否检查 Quantum、规则编号、文件与行号、依赖链、作者身份来源、实际读取字节的 SHA256 与扫描结束时 SHA256。`InputsChanged` 表示扫描期间发生源文件变化；这是本次时点检查，不能据此宣称其它窗口的工作区已冻结。退出码 0 为本次范围无违规，1 为架构违规；已枚举的输入在读取前被并行删除时退出码为 2，报告 `ScanCompleted=false`，不将部分结果当作通过。

## 规则与解析

`rules.json` 是唯一规则表。它覆盖全部 BTSMTL 程序集与工具、核心合同/运行时、领域合同、Shared 和 Quantum.Simulation；按真实 asmdef 名称解析 GUID 引用，按项目路径解析 ProjectReference 与本地 imports，按 package.json 验证依赖方向与包环。合同的允许引用表约束直接依赖；禁止的实现依赖沿完整依赖链检查。构建期 analyzer 不作为运行程序集引用。

源码按最近的 asmdef、asmref 或独立工具 csproj 归属，使用 Roslyn 读取声明、类型引用、别名、枚举成员与字符串。Unity 不导入的 `~` 目录、构建输出和版本管理目录不作为程序集输入。条件编译分支全部纳入扫描，注释不作为代码；正式数值类型的既有命名空间与同名枚举成员不被当作已删除类型。角色规则保留已知项目角色；完整工作区另外从 `Assets/Configs/Character` 的角色目录和资产 `m_AbilityId` 收集实际作者身份。技能名只在技能身份常量或按该身份比较/分派的位置检查，普通同名端口不等同于技能。

唯一核心指令枚举为 `BTSMTL.Orchestration.OrchestrationOperationCode`，声明见 [OrchestrationOperationCode.cs](../../Packages/com.btsmtl/Runtime/Orchestration/Contracts/Program/OrchestrationOperationCode.cs)。旧 `ThirdPersonSimulation.SimulationOperationCode` 已退役。扫描器对正式枚举的声明、成员访问、别名及 `using static` 检查规则表中的 43 个已删除领域指令；领域能力通过正式 Extension 合同接入，不重新加入核心枚举。

当前已删除类型表包含 893 个完整类型名及泛型元数，并补齐混合文件中删除的旧执行合同。迁入 Shared、Movement、独立领域合同及中立作者合同且保留全名的唯一正式类型不属于退役类型；判定依据是实际声明及其归属，不能把迁源当作删除。新增退役类型时更新同一清单。规则没有迁移白名单；当前违规由对应模块改正。

## 当前检查边界

完整输入的 Quantum 规则仍按 D12／D13 禁止 Unity 与表现依赖。实际官方 Quantum.Simulation 含 Unity 资产／Inspector 接入和条件编译代码；是否把规则限定到项目纯模拟程序集尚待用户决定，当前没有改规则、排除厂商分支或添加豁免。SDK-only 不扫描这个边界，其成功不能代替 Client 完整检查。

当前工具源码已连同正式枚举检查与 893 项清单独立编译，并完成新的 SDK-only 实际扫描。此前 Client 完整扫描与正式解决方案目标也已执行；这些结果仅对应各次报告中的工具版本和输入字节，尚未用本轮工具重跑 Client 完整检查。并行模块仍在清理时，报告的违规数量和输入变化只说明当次状态，不能把工具编译成功当作依赖规则通过。
