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
            OrchestrationStateBinding.ClearPayloads(m_Plan, memory);
            OrchestrationStateSegment frameSegment = plan.Segment(OrchestrationStateSegmentRole.Frames);
            memory.Slice(frameSegment.OffsetBytes, frameSegment.SizeBytes).Clear();
            var frames = new OrchestrationExecutionFrames<TValue>(plan, memory);
            int count = 0;
            for (int sourceSlot = source.FrameSlot; sourceSlot >= 0; count++)
            {
                ref readonly OrchestrationExecutionFrameState header = ref source.Frames.Header(sourceSlot);
                frames.Header(count) = header;
                frames.Header(count).ParentFrameIndex = header.ParentFrameIndex < 0 ? -1 : count + 1;
                int sourceProgram = source.Frames.Plan.ProgramIndex(header.SkillContentId);
                int destinationProgram = plan.ProgramIndex(header.SkillContentId);
                foreach (OrchestrationStateSegment segment in plan.Segments)
                {
                    if (!segment.PerFrame || segment.Role == OrchestrationStateSegmentRole.Frames)
                        continue;
                    OrchestrationStateSegment sourceSegment = source.Frames.Plan.Segment(segment.Role, segment.ValueKind);
                    OrchestrationStateRegion<byte> sourceRegion = source.Frames.Plan.PayloadRegion(in sourceSegment,
                        sourceSlot, sourceProgram, header.OwnerOperationIndex);
                    OrchestrationStateRegion<byte> destinationRegion = plan.PayloadRegion(in segment,
                        count, destinationProgram, header.OwnerOperationIndex);
                    source.Frames.Memory.Slice(sourceRegion.OffsetBytes, sourceRegion.SizeBytes)
                        .CopyTo(memory.Slice(destinationRegion.OffsetBytes, destinationRegion.SizeBytes));
                }
                sourceSlot = header.ParentFrameIndex;
            }
            OrchestrationStateSegment roster = plan.Segment(OrchestrationStateSegmentRole.Roster);
            SpanReinterpret.Cast<byte, OrchestrationExecutionRosterState>(
                memory.Slice(roster.OffsetBytes, roster.SizeBytes))[0].Count = count;
            foreach (OrchestrationStateSegment segment in plan.Segments)
            {
                if (segment.PerFrame || segment.Role == OrchestrationStateSegmentRole.Frames ||
                    segment.Role == OrchestrationStateSegmentRole.Roster)
                    continue;
                OrchestrationStateSegment sourceSegment = source.SharedFrames.Plan.Segment(segment.Role, segment.ValueKind);
                source.SharedFrames.Memory.Slice(sourceSegment.OffsetBytes, sourceSegment.SizeBytes)
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
