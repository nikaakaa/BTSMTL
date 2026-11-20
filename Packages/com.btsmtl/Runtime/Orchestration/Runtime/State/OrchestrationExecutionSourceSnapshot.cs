using System;
using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationExecutionSourceSnapshot<TValue> where TValue : unmanaged
    {
        readonly OrchestrationPreparedStateLayout<TValue> m_Plan;
        ulong m_Tick;
        OrchestrationStopContext m_StopContext;

        public OrchestrationExecutionSourceSnapshot(OrchestrationPreparedStateLayout<TValue> plan)
        {
            m_Plan = plan;
        }

        public int StorageSizeBytes => m_Plan.Layout.SizeBytes;

        public void Capture(Span<byte> memory, in OrchestrationExecutionSource<TValue> source)
        {
            OrchestrationStateLayoutView plan = m_Plan.View;
            ReadOnlyOrchestrationExecutionFrames<TValue> sourceFrames = source.Frames;
            OrchestrationStateLayoutView sourcePlan = sourceFrames.Plan;
            ReadOnlySpan<byte> sourceMemory = sourceFrames.Memory;
            var frames = new OrchestrationExecutionFrames<TValue>(plan, memory);
            int previousCount = frames.Count;
            int count = 0;
            for (int sourceSlot = source.FrameSlot; sourceSlot >= 0; count++)
            {
                ref readonly OrchestrationExecutionFrameState header = ref sourceFrames.Header(sourceSlot);
                ref OrchestrationExecutionFrameState destination = ref frames.Header(count);
                frames.ClearReplacedPayloads(count, in destination, header.SkillContentId, header.OwnerOperationIndex);
                destination = header;
                destination.ParentFrameIndex = header.ParentFrameIndex < 0 ? -1 : count + 1;
                ReadOnlySpan<int> sizes = sourcePlan.PayloadSizesForSkill(header.SkillContentId, header.OwnerOperationIndex);
                var sourcePayloads = sourcePlan.PayloadRanges(sourceSlot, sizes);
                var destinationPayloads = plan.PayloadRanges(count, sizes);
                sourcePayloads.CopyTo(in destinationPayloads, sourceMemory, memory);
                sourceSlot = header.ParentFrameIndex;
            }
            for (int slot = count; slot < previousCount; slot++)
            {
                ref OrchestrationExecutionFrameState retired = ref frames.Header(slot);
                frames.ClearReplacedPayloads(slot, in retired, 0, -1);
                retired = default;
            }
            OrchestrationStateSegment roster = plan.Segment(OrchestrationStateSegmentRole.Roster);
            SpanReinterpret.Cast<byte, OrchestrationExecutionRosterState>(
                memory.Slice(roster.OffsetBytes, roster.SizeBytes))[0].Count = count;
            ReadOnlyOrchestrationExecutionFrames<TValue> sharedFrames = source.SharedFrames;
            OrchestrationStateLayoutView sharedPlan = sharedFrames.Plan;
            ReadOnlySpan<byte> sharedMemory = sharedFrames.Memory;
            foreach (OrchestrationStateSegment segment in plan.Segments)
            {
                if (segment.PerFrame || segment.Role == OrchestrationStateSegmentRole.Frames ||
                    segment.Role == OrchestrationStateSegmentRole.Roster)
                    continue;
                OrchestrationStateSegment sourceSegment = sharedPlan.Segment(segment.Role, segment.ValueKind);
                sharedMemory.Slice(sourceSegment.OffsetBytes, sourceSegment.SizeBytes)
                    .CopyTo(memory.Slice(segment.OffsetBytes, segment.SizeBytes));
            }
            m_Tick = source.Tick;
            source.StopContextStorage.CopyTo(MemoryMarshal.CreateSpan(ref m_StopContext, 1));
        }

        public OrchestrationExecutionSource<TValue> Borrow(ReadOnlySpan<byte> memory)
        {
            var frames = new ReadOnlyOrchestrationExecutionFrames<TValue>(m_Plan.View, memory);
            return new OrchestrationExecutionSource<TValue>(frames, 0, m_Tick,
                MemoryMarshal.CreateSpan(ref m_StopContext, 1));
        }

        public void WriteRecordedState<TWriter>(ReadOnlySpan<byte> memory, ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(m_Tick);
            writer.WriteSpan<OrchestrationStopContext>(MemoryMarshal.CreateSpan(ref m_StopContext, 1));
            foreach (OrchestrationStateRegion<byte> region in OrchestrationStateBinding.ValidRanges(m_Plan, memory))
                writer.WriteSpan<byte>(memory.Slice(region.OffsetBytes, region.SizeBytes));
        }

        public void RestoreRecordedState(Span<byte> memory, ref RecordedRuntimeStateReader reader)
        {
            OrchestrationStateBinding.ClearPayloads(m_Plan, memory);
            m_Tick = reader.Read<ulong>();
            reader.ReadSpan<OrchestrationStopContext>(1).CopyTo(MemoryMarshal.CreateSpan(ref m_StopContext, 1));
            foreach (OrchestrationStateRegion<byte> region in OrchestrationStateBinding.ValidRanges(m_Plan, memory))
                reader.ReadSpan<byte>(region.SizeBytes).CopyTo(memory.Slice(region.OffsetBytes, region.SizeBytes));
        }
    }
}
