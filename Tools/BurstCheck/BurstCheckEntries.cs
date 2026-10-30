using System;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;
using Unity.Burst;

namespace BTSMTL.BurstCheck
{
    // 推进入口与 Timeline 逻辑推进入口经检查插头实例化；Timeline 派发与逻辑采样由宿主插头的 Tick 进入。
    [BurstCompile]
    public static unsafe partial class BurstCheckEntries
    {
        static class Run<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TProjection>
            where TValue : unmanaged
            where TScalar : unmanaged
            where TVector2 : unmanaged
            where TVector3 : unmanaged
            where TYaw : unmanaged
            where TQuaternion : unmanaged
            where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
            where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
            where TProjection : struct, IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar>
        {
            public static OrchestrationOperationResult Advance(BurstCheckMemory* memory, int operationIndex, ulong tick)
            {
                var input = new OrchestrationFrameInput<TValue>(tick, 1, ReadOnlySpan<TValue>.Empty,
                    ReadOnlySpan<int>.Empty);
                var context = new BurstCheckContext();
                var cursor = Cursor(memory, in input, in context);
                return cursor.Advance(new OrchestrationExecutionEntry(operationIndex, default));
            }

            public static void ForceStop(BurstCheckMemory* memory, int operationIndex, ulong tick)
            {
                var input = new OrchestrationFrameInput<TValue>(tick, 1, ReadOnlySpan<TValue>.Empty,
                    ReadOnlySpan<int>.Empty);
                var context = new BurstCheckContext();
                var cursor = Cursor(memory, in input, in context);
                cursor.ForceStop(operationIndex, OrchestrationStopContext.ParentStop(operationIndex));
            }

            static OrchestrationExecutionCursor<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                BurstCheckContext, OrchestrationProductDiagnostics<TValue>, OrchestrationArrayValueStorage<TValue>,
                BurstCheckHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TProjection>>
                Cursor(BurstCheckMemory* memory, in OrchestrationFrameInput<TValue> input, in BurstCheckContext context)
            {
                var ranks = new ReadOnlySpan<int>(memory->IdentityRanks, memory->IdentityRankCount);
                OrchestrationCandidateState<TValue> state = OrchestrationStateBinding.BindCandidate<TValue>(
                    new OrchestrationStateLayoutView(new ReadOnlySpan<byte>(memory->StateLayout, memory->StateLayoutLength)),
                    new Span<byte>(memory->State, memory->StateLength));
                return state.CreateCursor<TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, BurstCheckContext,
                    OrchestrationProductDiagnostics<TValue>, OrchestrationArrayValueStorage<TValue>,
                    BurstCheckHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TProjection>>(
                    new OrchestrationProgramView<TValue>(new ReadOnlySpan<byte>(memory->Program, memory->ProgramLength)),
                    new OrchestrationExecutionScratch<TValue>(new Span<byte>(memory->Scratch, memory->ScratchLength)),
                    0, ReadOnlySpan<int>.Empty, in input, new OrchestrationIdentityOrdinalView(ranks, ranks, ranks),
                    in context, new BurstCheckHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues,
                        TProjection>(memory), default);
            }
        }
    }
}
