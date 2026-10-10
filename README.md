# BTSMTL 源码包

BTSMTL 的唯一源码仓库。Unity 作者工具、Timeline 共用运行核心、Unity 宿主、EventGraph 和诊断工具位于 `Packages/com.btsmtl`，保留原命名空间、程序集名称及资源 GUID。

## Unity 接入

仓库与 `3C` 放在同一父目录。3C 客户端的 `Packages/manifest.json` 引用：

```json
"com.btsmtl": "file:../../../../../BTSMTL/Packages/com.btsmtl"
```

Unity 通过包内原有 asmdef 编译源码。编辑源码直接修改本仓库，3C 的 Assets 中不保留另一份 BTSMTL。角色资产、生成的 C# authoring、Workbench 角色装配及产品发布入口仍由 3C 持有。

## 普通 .NET 接入

3C 的 `Tools/ThirdPersonSimulation.Portable/ThirdPersonSimulation.Timeline/ThirdPersonSimulation.Timeline.csproj` 直接编译本包 `Timeline/Runtime/**/*.cs`，与 Unity 的 `BTSMTL.Timeline.Runtime` 使用同一份实现。

输入由宿主提供正式 Timeline 内容、逻辑时间、播放控制及 TreeClip 调用者；共用核心推进、提交或丢弃候选，并输出播放结果与快照；玩法和表现由各端宿主消费。普通 .NET 编译范围不包含 Unity 作者对象、Editor 或 Unity 组件。

## 源码职责

| 目录 | 职责 |
| --- | --- |
| `Timeline/Runtime` | 不引用 Unity 的时间推进、内容求值、TreeClip 生命周期、快照及普通 .NET 适配器 |
| `Timeline/Scripts` | Unity Timeline 作者对象、树节点和运行宿主装配 |
| `Timeline/Unity` | 场景表现组件 |
| `Timeline/Editor` | Timeline 与 TreeClip 编辑器 |
| `Authoring`、`Scripts`、`EventGraphs` | 共用作者合同、图节点及 Unity EventGraph 接入 |
| `Diagnostics`、`Editor` | 运行诊断、编辑窗口和界面资源 |

## 依赖边界

共用 Timeline 核心继续依赖 3C 的 `ThirdPerson.Shared.Contracts`、`ThirdPersonSimulation.Core`、`ThirdPersonSimulation.Fixed` 和 `ThirdPersonSimulation.Float32`；内容协议、数值类型及技能执行合同由这些现有程序集持有。

Unity 作者和编辑器还依赖宿主提供的 NodeCanvas、FlowCanvas、ParadoxNotion、Slate、角色 RootMotion、Control 作者合同、Gameplay、Camera 和 Simulation.Unity 程序集。`com.unity.ugui` 由包声明。这次迁移改变源码归属与引用位置，未把宿主合同复制进包；单独克隆本仓库仍需提供上述依赖。

## 开发约束

- Unity 与普通 .NET 消费同一核心源码，不新增另一套推进算法或旧路径副本。
- 移动源码和资源时保留 `.meta` GUID；高频运行链路保持 0 GC。
- 用户已有的未提交修改保持未提交，避免迁移提交混入并行开发内容。
- 编译使用 `--disable-build-servers /nr:false /p:UseSharedCompilation=false`，结束后执行 `dotnet build-server shutdown`。
