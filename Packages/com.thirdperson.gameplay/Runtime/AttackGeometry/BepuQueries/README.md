# 官方 FP 凸体查询依赖

来源为 [sam-vdp/bepuphysics1int 固定提交](https://github.com/sam-vdp/bepuphysics1int/tree/9237daa68c3014fd7c2e93c6a99326ba5248d60b)。现有 MPR、GJK 和项目连续旋转查询已迁为官方 `Photon.Deterministic.FP`、`FPVector3`、`FPQuaternion` 与 `FPMath`，没有 Fix64 或 Q32.32 往返。

本目录只做球、胶囊、盒、凸包和准备后的凸部件求交，与 `ThirdPersonSimulation.FPGameplay.FPSweepQueryBackend` 一同编入 `ThirdPersonSimulation.AttackGeometry`。没有刚体世界、运动积分、约束、KCC、Scene、Entity 或自主 Tick。官方 Quantum／KCC 源码与 DLL 保持原版；算法源码只使用 `Quantum.Deterministic.dll`，所在几何程序集另引用 Shared 数值合同和 QuantumNumerics，不依赖 Unity 或完整 Gameplay。原 BEPU 许可见 `LICENSE.md`，已删除 FixedMath.NET 实现的原许可仍保留在 `FixedMath.Net/LICENSE.txt`，迁移保留许可文件原字节。

## 原算法与保留行为

- 固定朝向的初始相交和平移仍走 MPR；双方连续旋转仍走 GJK 最近点、形状极值点分离平面和角速度界的保守推进。没有改成 SDK ShapeCast，也没有增加离散 Overlap 采样或算法失败后的重试。
- `RigidSweepMotion` 保留相对四元数、最短恒角速度旋转弧和精确端点。官方四元数乘法的顺序为父旋转乘局部旋转；相对旋转为终点乘起点共轭。部件局部偏移随根旋转，角速度界继续包含该偏移和形状半径。
- MPR 内／外迭代限制仍为 15／15，GJK 与保守推进仍各为 64，GJK 多迭代收敛门槛仍为 8。未收敛仍向正式查询返回 `NativeIterationLimit`，不把失败变成未命中。
- 球和胶囊的 margin 仍等于作者半径，盒和凸包 margin 为零。盒保留完整作者半尺寸；凸包仍由上层准备重新定心，部件局部中心仍参与运动。
- 初始相交的 GJK 贡献点、margin 相交的加权见证位置和 MPR 的位置可用标记保留。适配器仍只在实际可用时发布 Point／Normal，MPR 法线仍取反为目标朝向查询体。
- 删除查询不使用的质量、惯性、包围盒、矩阵、RayTest、旧向量／旋转、Fix64 与自研三角 LUT。原 GJK 缓存只在单次调用中创建，返回后从未读取；现改为同一初始单纯形直接求解，保留贡献点，不再回写无人读取的缓存。

## FP 量化、容差与运算范围

FP 步长为 `1/65536`。接触距离使用官方 `FP.EN4`，Raw 为 7，即 `0.0001068115234375`；原作者意义仍是约 `0.0001` 的接触收敛距离，不是任意输入的全域 TOI 误差保证。

原 MPR 的 `1e-7`／`1e-9` 和 GJK 的 `1e-8`／`1e-7` 在 FP 中无法表示。MPR 门面点积收敛、GJK 进展及距离收敛系数采用 `FP.SmallestNonZero`（Raw 1）；GJK 仍按原 `errorTolerance` 缩放进展／收敛系数。核心接触使用官方长度结果与一个 Raw 的线性距离比较，不把平方量化为零当作核心相交。

方向归一化、角运动轴、margin 扩张和接触距离使用官方 `FPVector3.Normalize`；需要长度时使用其 `out FP length`。普通 `Magnitude` 先量化平方，再开方，会丢失小距离。平移射线使用同一官方单位方向及线性长度，不把小于一个平方量化步长的实际位移改成静止。MPR 重心权重和 GJK 三角形权重直接做分子／分母，避免先计算倒数就量化为零。

不能以 `FP.MaxValue` 声明乘法安全范围。SDK 的 FP 乘法先做 64 位 Raw 乘积，实际几何中还存在叉积、点积与六次量纲的四面体侧面判断。令 R 为该对形状实际使用的相对位置、位移、支持点及 MPR 扩张支持点长度上界，则 GJK 的 `AdotN * ADdotN` 保守上界为 `32 * R^6`。例如 R≤16 时该项≤536870912，尚低于普通 FP 乘法的约 2^31 结果量级限制；相关四次项和 Raw 归一化平方和也在 64 位中间范围内。这是本算法的一组充分条件，不是将世界坐标限定在16，也不是所有内容的最大安全范围。

正式准备需要从实际攻击／受击几何、部件偏移、双方运动和时段确认中间运算范围。还要确认小尺寸或退化几何在 FP 下的面积／体积与分母可表达；上述上界证明不覆盖下溢。查询没有新增运行期坐标钳制、备用精度、旧 Raw Reader 或自定义范围成功标记。本轮没有发布／回读完整 FP 内容，因此不能用这组静态推导宣称整份产品的数值范围已确认。源码中保留的 `FP.MaxValue` 仅作最近距离及失败时间哨兵，不作为乘法输入范围。

## 正式适配与构建

后端身份为 `bepuphysics1int/9237daa68c3014fd7c2e93c6a99326ba5248d60b/fp-q48.16-mpr-gjk-angular-query-v4`，数值描述使用已有 `QuantumSimulationNumericProfile.Value`（`quantum-fp-q48.16`／ABI1）。旧 v3／Q32.32 内容和结果不能按同一身份继续使用。

唯一 portable 工程为 `Projects/ThirdPersonSimulation.AttackGeometry/ThirdPersonSimulation.AttackGeometry.csproj`，与同名 asmdef 编译同一源码。原 `ThirdPerson.BepuQueries` 工程和 asmdef 已删除。宿主通过 `QuantumDeterministicPath` 提供已安装的官方数学 DLL：

```powershell
$taskQuantumDeterministicPath = '宿主现有 Quantum.Deterministic.dll 的绝对路径'
try {
  dotnet build Projects/ThirdPersonSimulation.AttackGeometry/ThirdPersonSimulation.AttackGeometry.csproj --disable-build-servers /nr:false /p:UseSharedCompilation=false /m:1 "/p:QuantumDeterministicPath=$taskQuantumDeterministicPath"
} finally {
  dotnet build-server shutdown
}
```

准备阶段创建形状与顶点数组；正常查询继续只用已有形状和结果缓冲，以及值类型局部量。源码／编译检查不能代替 FP 运行时 GC、CPU、命中边界或跨平台检查。当前阶段与失败记录见 3C 项目 `docs/测试/命中判定查询核对20261001.md` 的 FP 迁移段。
