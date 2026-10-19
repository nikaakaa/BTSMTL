using System;
using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationExecutionSourceSnapshot<TValue> where TValue : unmanaged
    {
        readonly OrchestrationPreparedStateLayout<TValue> m_Plan;
        readonly byte[] m_Memory;
        ulong m_Tick;
        OrchestrationStopContext m_StopContext;

        public OrchestrationExecutionSourceSnapshot(OrchestrationPreparedStateLayout<TValue> plan)
        {
            m_Plan = plan;
            m_Memory = new byte[plan.Layout.SizeBytes];
        }

        public int StorageSizeBytes => m_Memory.Length;

        public void Capture(OrchestrationExecutionSource<TValue> source)
        {
            OrchestrationStateBinding.ClearPayloads(m_Plan, m_Memory);
            OrchestrationStateSegment frameSegment = m_Plan.Segment(OrchestrationStateSegmentRole.Frames);
            m_Memory.AsSpan(frameSegment.OffsetBytes, frameSegment.SizeBytes).Clear();
            var frames = new OrchestrationExecutionFrames<TValue>(m_Plan, m_Memory);
            int count = 0;
            for (int sourceSlot = source.FrameSlot; sourceSlot >= 0; count++)
            {
                ref readonly OrchestrationExecutionFrameState header = ref source.Frames.Header(sourceSlot);
                frames.Header(count) = header;
                frames.Header(count).ParentFrameIndex = header.ParentFrameIndex < 0 ? -1 : count + 1;
                int sourceProgram = source.Frames.Plan.ProgramIndex(header.SkillContentId);
                int destinationProgram = m_Plan.ProgramIndex(header.SkillContentId);
                foreach (OrchestrationStateSegment segment in m_Plan.Segments.Span)
                {
                    if (!segment.PerFrame || segment.Role == OrchestrationStateSegmentRole.Frames)
                        continue;
                    OrchestrationStateSegment sourceSegment = source.Frames.Plan.Segment(segment.Role, segment.ValueKind);
                    OrchestrationStateRegion<byte> sourceRegion = source.Frames.Plan.PayloadRegion(in sourceSegment,
                        sourceSlot, sourceProgram, header.OwnerOperationIndex);
                    OrchestrationStateRegion<byte> destinationRegion = m_Plan.PayloadRegion(in segment,
                        count, destinationProgram, header.OwnerOperationIndex);
                    source.Frames.Memory.Slice(sourceRegion.OffsetBytes, sourceRegion.SizeBytes)
                        .CopyTo(m_Memory.AsSpan(destinationRegion.OffsetBytes, destinationRegion.SizeBytes));
                }
                sourceSlot = header.ParentFrameIndex;
            }
            OrchestrationStateSegment roster = m_Plan.Segment(OrchestrationStateSegmentRole.Roster);
            MemoryMarshal.Cast<byte, OrchestrationExecutionRosterState>(
                m_Memory.AsSpan(roster.OffsetBytes, roster.SizeBytes))[0].Count = count;
            foreach (OrchestrationStateSegment segment in m_Plan.Segments.Span)
            {
                if (segment.PerFrame || segment.Role == OrchestrationStateSegmentRole.Frames ||
                    segment.Role == OrchestrationStateSegmentRole.Roster)
                    continue;
                OrchestrationStateSegment sourceSegment = source.Frames.Plan.Segment(segment.Role, segment.ValueKind);
                source.Frames.Memory.Slice(sourceSegment.OffsetBytes, sourceSegment.SizeBytes)
                    .CopyTo(m_Memory.AsSpan(segment.OffsetBytes, segment.SizeBytes));
            }
            m_Tick = source.Tick;
            m_StopContext = source.StopContext;
        }

        public OrchestrationExecutionSource<TValue> Borrow()
        {
            var frames = new ReadOnlyOrchestrationExecutionFrames<TValue>(m_Plan, m_Memory);
            return new OrchestrationExecutionSource<TValue>(frames, 0, m_Tick, in m_StopContext);
        }

        public void WriteRecordedState<TWriter>(ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(m_Tick);
            writer.Write(m_StopContext);
            foreach (OrchestrationStateRegion<byte> region in OrchestrationStateBinding.ValidRanges(m_Plan, m_Memory))
                writer.WriteSpan<byte>(m_Memory.AsSpan(region.OffsetBytes, region.SizeBytes));
        }

        public void RestoreRecordedState(ref RecordedRuntimeStateReader reader)
        {
            OrchestrationStateBinding.ClearPayloads(m_Plan, m_Memory);
            m_Tick = reader.Read<ulong>();
            m_StopContext = reader.Read<OrchestrationStopContext>();
            foreach (OrchestrationStateRegion<byte> region in OrchestrationStateBinding.ValidRanges(m_Plan, m_Memory))
                reader.ReadSpan<byte>(region.SizeBytes).CopyTo(m_Memory.AsSpan(region.OffsetBytes, region.SizeBytes));
        }
    }
}
