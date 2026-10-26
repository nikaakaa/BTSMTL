# BTSMTL 网页作者工作台

HTML 和 CLI 从本地服务读取项目正式作者目录。共同库持有根、图、稳定引用、无损作者值与图修改候选；页面不再消费原型 localStorage，也不维护七节点清单或独立时间草稿。原浏览器草稿及已导出的原型文件保留。

## 启动

需要支持构建并运行 .NET 8 的 SDK 与运行时。在仓库根目录运行：

```powershell
./Tools/BTSMTL.WebWorkbench/Start.ps1 -WorkspaceRoot D:/Unity_Project_1/3C/Authoring/BTSMTL/Documents
```

`WorkspaceRoot` 必须明确指定已有的项目作者目录。浏览器打开 <http://127.0.0.1:5367>。脚本完成编译并关闭构建服务器后启动服务，`Ctrl+C` 停止。`-Port` 指定端口；服务只监听本机。构建位于本仓库 `.codex/tmp/web-workbench/build`，进程临时目录为 `.codex/tmp/system-temp/`。

`-AnimationLibrary` 指定正式网页动画清单。打开 `/resources.html` 可选择清单内片段、播放、拖动时间及调整模型视角。three.js 及其加载器使用锁定的本地 npm 依赖；启动脚本通过 `npm ci` 还原。服务先按清单哈希核对 glb，再按内容版本提供分段读取；页面明确显示资源版本与近似表现范围。动画资源页尚未接到真实会话的帧和播放请求。

HTML、CSS、JavaScript 修改后刷新页面即可看到结果；修改 C# 接口后重启服务以重新编译。

## 当前操作

选择内容根及其文件，查看实际图拓扑、作者字段、引用、源文和读取修订。画布支持平移、缩放与对象选择。非法外部根显示定位错误，并可查看原始字节对应的 UTF-8 原文；服务不恢复旧缓存覆盖文件。

当前已接通读取。完整字段合同、领域接收、文件提交、Timeline 编辑、编译发布、Unity 派生与独立运行仍在接入，页面据实显示未完成阶段。文件可读不代表内容已通过领域规则或已被游戏采用；尚未完成技能来源迁移。

## 接口与职责

| 接口 | 输入与实际处理 | 输出 |
| --- | --- | --- |
| `GET /api/workspace` | 启动参数指定的目录 | 实际作者目录和文档格式版本 |
| `GET /api/workspace/roots?offset=0&limit=100` | 读取根清单，分页上限 200 | 稳定身份、根修订、直接依赖及文件问题 |
| `GET /api/workspace/root?path=Root/root.json` | 按根的明确文件清单读取 | 根正文、全部文件原文及实际字节修订 |
| `GET /api/workspace/source?path=Root/root.json` | 读取限定目录内的精确文件 | 原文、路径、SHA-256，不要求文件已合法 |
| `GET /api/workspace/references?rootId=ID&offset=0&limit=100` | 读取根的正式外部引用 | 分页反向引用 |

`AuthoringWorkspaceDirectory` 负责文件范围和读取；`WorkspaceApi` 只投影 HTTP 协议；`wwwroot/documents.js` 展示同一内容。根内文件路径不能越出 owner 目录；布局与业务文档分别持有。64 位数值和定点原值在作者值中使用文本，不经过 JavaScript Number。

图修改候选绑定整个根的原始文件修订。`AuthoringGraphCommit` 按物理文件协调提交，过期请求返回冲突路径、当前修订和相关对象；共同文件事务处理创建、替换、删除及失败恢复，3C 原作者代码输出器也已改接该事务。撤销候选按原反向操作比较实际字段和引用，保留后续无关改动；尚未接到两端撤销入口。领域接收仍是候选创建的必需输入，当前未开放写入 HTTP 接口。

原 `GraphAuthoringGeneratedContract`、成员绑定和字段描述已纳入普通 .NET 作者库。字段类型来自原成员，资源绑定回调使用所属平台的实际资源类型；Unity Inspector 在平台边界转换原对象。生成器和网页服务本轮构建均为 0 警告、0 错误；这一步尚未迁出完整技能节点及领域接收，不代表网页已可修改技能。

## 已检查范围

共同模型、确定性读写、修改候选、修订提交与网页服务已在普通 .NET 构建通过，0 警告、0 错误；JavaScript 语法检查通过。共同文件事务已通过临时文件实际创建、替换、删除、退役空目录及中途失败恢复检查；这不代替正式作者保存链验收。107 段动画清单与 glb 分段读取已通过实际 HTTP 检查。新页面尚未完成实际浏览器检查，未进行正式技能迁移和独立运行验收。旧原型的交互检查属于已删除的旧入口，不沿用为新页面的验证结果。
