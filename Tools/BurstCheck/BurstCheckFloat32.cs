using BTSMTL.Orchestration;
using Unity.Burst;

namespace BTSMTL.BurstCheck
{
    // Float32 数值目标全部来自 BTSMTL 自身，是正式独立编译的必过检查。
    public static unsafe partial class BurstCheckEntries
    {
        [BurstCompile]
        public static int AdvanceFloat32(BurstCheckMemory* memory, int operationIndex, ulong tick) =>
            (int)Run<Float32OrchestrationNumerics>.Advance(memory, operationIndex, tick);

        [BurstCompile]
        public static void ForceStopFloat32(BurstCheckMemory* memory, int operationIndex, ulong tick) =>
            Run<Float32OrchestrationNumerics>.ForceStop(memory, operationIndex, tick);
    }
}
