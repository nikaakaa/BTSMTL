using System;
using BTSMTL.Orchestration;
using Unity.Burst;

namespace BTSMTL.BurstCheck
{
    // 推进入口与 Timeline 逻辑推进入口经检查插头实例化；Timeline 派发与逻辑采样由宿主插头的 Tick 进入。
    [BurstCompile]
    public static unsafe partial class BurstCheckEntries
    {
        static class Run<TNumerics> where TNumerics : struct, IOrchestrationNumerics<Float32OrchestrationValue>
        {
            public static OrchestrationOperationResult Advance(BurstCheckMemory* memory, int operationIndex, ulong tick)
            {
                var input = new OrchestrationFrameInput<Float32OrchestrationValue>(tick, 1,
                    ReadOnlySpan<Float32OrchestrationValue>.Empty, ReadOnlySpan<int>.Empty);
                var cursor = Cursor(memory, in input);
                return cursor.Advance(new OrchestrationExecutionEntry(operationIndex, default));
            }

            public static void ForceStop(BurstCheckMemory* memory, int operationIndex, ulong tick)
            {
                var input = new OrchestrationFrameInput<Float32OrchestrationValue>(tick, 1,
                    ReadOnlySpan<Float32OrchestrationValue>.Empty, ReadOnlySpan<int>.Empty);
                var cursor = Cursor(memory, in input);
                cursor.ForceStop(operationIndex, OrchestrationStopContext.ParentStop(operationIndex));
            }

            static OrchestrationExecutionCursor<Float32OrchestrationValue, TNumerics, BurstCheckHost> Cursor(
                BurstCheckMemory* memory, in OrchestrationFrameInput<Float32OrchestrationValue> input)
            {
                var ranks = new ReadOnlySpan<int>(memory->IdentityRanks, memory->IdentityRankCount);
                OrchestrationCandidateState<Float32OrchestrationValue> state =
                    OrchestrationStateBinding.BindCandidate<Float32OrchestrationValue>(
                        new OrchestrationStateLayoutView(new ReadOnlySpan<byte>(memory->StateLayout, memory->StateLayoutLength)),
                        new Span<byte>(memory->State, memory->StateLength));
                return state.CreateCursor<TNumerics, BurstCheckHost>(
                    new OrchestrationProgramView<Float32OrchestrationValue>(
                        new ReadOnlySpan<byte>(memory->Program, memory->ProgramLength)),
                    new OrchestrationExecutionScratch<Float32OrchestrationValue>(
                        new Span<byte>(memory->Scratch, memory->ScratchLength)),
                    0, ReadOnlySpan<int>.Empty, in input, new OrchestrationIdentityOrdinalView(ranks, ranks, ranks),
                    new BurstCheckHost(memory));
            }
        }
    }
}
