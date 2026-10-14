# BEPU 定点查询依赖

来源：[sam-vdp/bepuphysics1int](https://github.com/sam-vdp/bepuphysics1int/tree/9237daa68c3014fd7c2e93c6a99326ba5248d60b)，固定提交 `9237daa68c3014fd7c2e93c6a99326ba5248d60b`。

本程序集只供 `FixedSweepQueryBackend` 调用球、胶囊、Box、凸体极值点、MPR 平移、GJK 最近点及连续旋转所需数学。没有 Space、Entity、Collidable、约束、世界更新或自主 Tick。上游 fork 及原 BEPU 许可保留在 `LICENSE.md`，FixedMath.NET 许可保留在 `FixedMath.Net/LICENSE.txt`。

## 源码裁剪

通过 Roslyn 语法树裁剪成员，保留上游形状及算法方法体，源文件名和目录保持上游布局：

- `EntityShape` 和球 / 胶囊 / Box 删除 `GetCollidableInstance`；形状的尺寸、极值点和边界计算保留。
- 形状删除 `RayTest`、扫掠 / 局部包围盒入口；没有引入 RayCast。
- `MPRToolbox` 保留 `AreShapesOverlapping`、所需局部 Overlap、`Sweep`、双形状 `LocalSweepCast`、`GetLocalPosition`、`AreSweptShapesIntersecting` 和双形状 `GetSweptExtremePoint`。
- `Toolbox` 保留数学字段和 `GetBarycentricCoordinates`，删除无消费者的方法；数学类型及 Fix64 / 原生三角 LUT 保留。
- 没有凸包生成、资源池、多线程或刚体世界依赖。
- 同日旋转扩展只增加 GJK 的 `GetClosestPoints` 与 `PairSimplex`，不带 RaySimplex、SimpleSimplex 或整套 GJK Cast。`QueryConvexHullShape` 消费已准备、已重新定心的凸体顶点，不生成凸分解。

## 查询状态增量

初版适配版本为 `mpr-query-v1`；旋转扩展版本为 `mpr-gjk-angular-query-v2`；当前接触见证点版本为 `mpr-gjk-angular-query-v3`。MPR 公开查询增加 `out bool iterationLimitReached`，沿原有循环条件记录未达到收敛条件而耗尽迭代的情况。上层拒绝该次查询结果，不把原生 false 当作成功未命中，也不接受耗尽迭代后得到的近似接触。

Sweep 增加 `out bool positionAvailable`。MPR Sweep 只写默认位置的分支仍标记为不可用。初始相交由正式相交算法判定，GJK 保留已求得的贡献单纯形；四面体包含原点时计算四个体积重心权重，输出双方形状的实际见证点，不再将双方位置清零。核心未相交而 margin 相交时，在最近点间按双方 margin 分配距离，得到位于共同体积内的点。初始相交没有唯一表面接触点，不发布虚构法线。

固定朝向请求的 MPR 初始相交成功后只补求 GJK 见证位置，MPR 耗尽迭代时不进入该步骤；旋转请求直接保留自身 GJK 见证位置。这不是算法失败后的重试或备用路径。后端依据实际位置可用性发布 Point 字段，正常扫入的时间与法线语义保持。MPR 的法线为 Minkowski 外法线，适配器取反，统一为目标朝向查询形状的法线。

MPR 迭代数固定为上游的 15 / 15，surface epsilon 为 `1e-7`，raycast surface epsilon 为 `1e-9`，Toolbox epsilon 为 `1 / 10000000`，均保留上游 Q32.32 量化结果并固定值。Box 的 CollisionMargin 在准备中明确设为零，球和胶囊的 margin 是其原生半径，不额外扩大作者几何。

## 连续旋转增量

`AngularSweepToolbox` 使用 GJK 最近点确定分离方向，用形状极值点确认分离平面间距，再按双方平移与角速度乘外接半径的闭合速度界保守推进。算法路线参考 [Bullet continuous convex collision](https://github.com/bulletphysics/bullet3/blob/master/src/BulletCollision/NarrowPhaseCollision/btContinuousConvexCollision.cpp) 的角速度界与保守推进，不复制其 C++ 实现。Fixed 全程使用本程序集的 Fix64。

`RigidSweepMotion` 以相对四元数的 `Atan2` 轴角表达最短恒角速度旋转弧，端点直接保留。`ConvexSweepMotion` 将部件局部位姿跟随根位姿旋转；角运动半径包括局部偏移。推进与 GJK 各最多 64 次，GJK 多迭代收敛参数为固定 8，PairSimplex 原生收敛阈值固定为 readonly。接触收敛距离为 `0.0001`，仍受 GJK 原生数值容差影响；没有声明全域 TOI 误差界。耗尽迭代明确报告失败，没有 GJK/MPR 互相重试或备用路径。

Box `OnShapeChanged` 的上游参数错误已修正：`ComputeDescription` 接收完整尺寸，原调用误传半尺寸，导致更新 margin 后的外接半径减半。此改动使描述与实际极值点几何一致，没有扩大作者 Box。

## 数值边界

项目 `FixedScalar.Raw` 与原生 `Fix64.RawValue` 直接映射，同为 Q32.32。原生乘除、舍入、溢出和求解容差仍由固定版本 BEPU 数学决定，与项目的 nearest-even / reject-overflow 行为不相同。此边界只服务查询输入 / 输出，不改变正式角色或世界状态，不经 float 求交，也不成为另一套项目数值目标。

该源码支持声明路径上的平移、刚性旋转、凸包与已准备凸部件的 Mesh。没有任意三角网格、变形 Mesh、自动凸分解或 Quantum 3 适配。跨机器与 Player 平台确定性仍须以相同后端版本和正式输入验证。
