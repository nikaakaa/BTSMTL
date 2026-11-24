using System;
using System.Runtime.InteropServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationExecutionSourceSnapshot<TValue> where TValue : unmanaged
    {
        readonly OrchestrationPreparedStateLayout<TValue> m_Plan;
        readonly OrchestrationStateRegion<ulong> m_Tick;
        readonly OrchestrationStateRegion<OrchestrationStopContext> m_StopContext;

        public OrchestrationExecutionSourceSnapshot(OrchestrationPreparedStateLayout<TValue> plan)
        {
            m_Plan = plan;
            int storageBytes = plan.Layout.SizeBytes;
            m_Tick = OrchestrationStateRegion<ulong>.Reserve(ref storageBytes, 1);
            m_StopContext = OrchestrationStateRegion<OrchestrationStopContext>.Reserve(ref storageBytes, 1);
            StorageSizeBytes = storageBytes;
        }

        public int StorageSizeBytes { get; }

        public void Capture(Span<byte> storage, in OrchestrationExecutionSource<TValue> source)
        {
            OrchestrationStateLayoutView plan = m_Plan.View;
            Span<byte> memory = storage.Slice(0, m_Plan.Layout.SizeBytes);
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
            StoreTick(storage, source.Tick);
            source.StopContextStorage.CopyTo(WritableStopContext(storage));
        }

        public OrchestrationExecutionSource<TValue> Borrow(ReadOnlySpan<byte> storage)
        {
            ReadOnlySpan<byte> memory = storage.Slice(0, m_Plan.Layout.SizeBytes);
            var frames = new ReadOnlyOrchestrationExecutionFrames<TValue>(m_Plan.View, memory);
            return new OrchestrationExecutionSource<TValue>(frames, 0, ReadTick(storage), ReadStopContext(storage));
        }

        public void WriteRecordedState<TWriter>(ReadOnlySpan<byte> storage, ref TWriter writer)
            where TWriter : struct, IRecordedRuntimeStateWriter
        {
            writer.Write(ReadTick(storage));
            writer.WriteSpan<OrchestrationStopContext>(ReadStopContext(storage));
            ReadOnlySpan<byte> memory = storage.Slice(0, m_Plan.Layout.SizeBytes);
            foreach (OrchestrationStateRegion<byte> region in OrchestrationStateBinding.ValidRanges(m_Plan, memory))
                writer.WriteSpan<byte>(memory.Slice(region.OffsetBytes, region.SizeBytes));
        }

        public void RestoreRecordedState(Span<byte> storage, ref RecordedRuntimeStateReader reader)
        {
            Span<byte> memory = storage.Slice(0, m_Plan.Layout.SizeBytes);
            OrchestrationStateBinding.ClearPayloads(m_Plan, memory);
            StoreTick(storage, reader.Read<ulong>());
            reader.ReadSpan<OrchestrationStopContext>(1).CopyTo(WritableStopContext(storage));
            foreach (OrchestrationStateRegion<byte> region in OrchestrationStateBinding.ValidRanges(m_Plan, memory))
                reader.ReadSpan<byte>(region.SizeBytes).CopyTo(memory.Slice(region.OffsetBytes, region.SizeBytes));
        }

        ulong ReadTick(ReadOnlySpan<byte> storage) => MemoryMarshal.Read<ulong>(storage.Slice(m_Tick.OffsetBytes));

        void StoreTick(Span<byte> storage, ulong tick) =>
            MemoryMarshal.Cast<byte, ulong>(storage.Slice(m_Tick.OffsetBytes, m_Tick.SizeBytes))[0] = tick;

        ReadOnlySpan<OrchestrationStopContext> ReadStopContext(ReadOnlySpan<byte> storage) =>
            MemoryMarshal.Cast<byte, OrchestrationStopContext>(storage.Slice(m_StopContext.OffsetBytes, m_StopContext.SizeBytes));

        Span<OrchestrationStopContext> WritableStopContext(Span<byte> storage) =>
            MemoryMarshal.Cast<byte, OrchestrationStopContext>(storage.Slice(m_StopContext.OffsetBytes, m_StopContext.SizeBytes));
    }
}
