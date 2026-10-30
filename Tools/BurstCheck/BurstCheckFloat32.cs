using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using Unity.Burst;

namespace BTSMTL.BurstCheck
{
    // Float32 数值目标全部来自 BTSMTL 自身，是正式独立编译的必过检查。
    public static unsafe partial class BurstCheckEntries
    {
        [BurstCompile]
        public static int AdvanceFloat32(BurstCheckMemory* memory, int operationIndex, ulong tick) =>
            (int)Run<Float32OrchestrationValue, Float32Scalar, Float32Vector2, Float32Vector3, Float32Yaw,
                Float32Quaternion, Float32SimulationMath, Float32OrchestrationValueAccess,
                BurstCheckFloat32Projection>.Advance(memory, operationIndex, tick);

        [BurstCompile]
        public static void ForceStopFloat32(BurstCheckMemory* memory, int operationIndex, ulong tick) =>
            Run<Float32OrchestrationValue, Float32Scalar, Float32Vector2, Float32Vector3, Float32Yaw,
                Float32Quaternion, Float32SimulationMath, Float32OrchestrationValueAccess,
                BurstCheckFloat32Projection>.ForceStop(memory, operationIndex, tick);
    }
}
