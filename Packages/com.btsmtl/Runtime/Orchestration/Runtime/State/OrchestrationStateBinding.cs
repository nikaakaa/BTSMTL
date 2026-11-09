using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public static unsafe class OrchestrationStateBinding
    {
        public static void RetireFrameOverrides<TValue>(ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<int> skillContentIds, Span<OrchestrationValueOverride<TValue>> currentOverrides,
            ulong tick) where TValue : unmanaged
        {
            // 宿主只对当前输入状态更代；冻结来源快照保留其原 tick。
            for (int programIndex = 0; programIndex < programs.Length; programIndex++)
            {
                OrchestrationProgram<TValue> program = programs[programIndex];
                for (int index = 0; index < currentOverrides.Length; index++)
                {
                    ref OrchestrationValueOverride<TValue> value = ref currentOverrides[index];
                    if (value.Mode != OrchestrationValueOverrideMode.None &&
                        value.Address.Kind == OrchestrationValueOverrideKind.Blackboard &&
                        value.Address.Owner.SkillContentId == skillContentIds[programIndex] &&
                        program.Blackboard(value.Address.Slot).ScopeKind == OrchestrationScopeKind.Frame &&
                        value.Address.Owner.InvocationGeneration != tick)
                        value.Retire();
                }
            }
        }

        public static OrchestrationCandidateState<TValue> BindCandidate<TValue>(
            OrchestrationPreparedStateLayout<TValue> plan, Span<byte> memory) where TValue : unmanaged =>
            BindCandidate<TValue>(plan.View, memory);

        // 纯数据宿主只持有布局镜像字节时使用的绑定入口。
        public static OrchestrationCandidateState<TValue> BindCandidate<TValue>(
            OrchestrationStateLayoutView plan, Span<byte> memory) where TValue : unmanaged =>
            new OrchestrationCandidateState<TValue>(new OrchestrationExecutionFrames<TValue>(plan, memory),
                new OrchestrationStateView(memory.Slice(plan.TimelineRegion.OffsetBytes,
                    plan.TimelineRegion.SizeBytes)));

        public static OrchestrationCommittedState<TValue> BindCommitted<TValue>(
            OrchestrationPreparedStateLayout<TValue> plan, ReadOnlySpan<byte> memory) where TValue : unmanaged =>
            BindCommitted<TValue>(plan.View, memory);

        public static OrchestrationCommittedState<TValue> BindCommitted<TValue>(
            OrchestrationStateLayoutView plan, ReadOnlySpan<byte> memory) where TValue : unmanaged =>
            new OrchestrationCommittedState<TValue>(new ReadOnlyOrchestrationExecutionFrames<TValue>(plan, memory),
                new ReadOnlyOrchestrationStateView(memory.Slice(plan.TimelineRegion.OffsetBytes,
                    plan.TimelineRegion.SizeBytes)));

        public static OrchestrationStateValidRanges<TValue> ValidRanges<TValue>(
            OrchestrationPreparedStateLayout<TValue> plan, ReadOnlySpan<byte> memory) where TValue : unmanaged =>
            new OrchestrationStateValidRanges<TValue>(plan.View, memory);

        public static OrchestrationStateValidRanges<TValue> ValidRanges<TValue>(
            OrchestrationStateLayoutView plan, ReadOnlySpan<byte> memory) where TValue : unmanaged =>
            new OrchestrationStateValidRanges<TValue>(plan, memory);

        public static void CopyMemory<TValue>(OrchestrationPreparedStateLayout<TValue> plan,
            ReadOnlySpan<byte> source, Span<byte> destination) where TValue : unmanaged
        {
            OrchestrationStateLayoutView layout = plan.View;
            var sourceFrames = new ReadOnlyOrchestrationExecutionFrames<TValue>(layout, source);
            var destinationFrames = new OrchestrationExecutionFrames<TValue>(layout, destination);
            for (int slot = 0; slot < destinationFrames.Capacity; slot++)
            {
                ref readonly OrchestrationExecutionFrameState previous = ref destinationFrames.Header(slot);
                if (previous.SkillContentId == 0)
                    continue;
                ref readonly OrchestrationExecutionFrameState next = ref sourceFrames.Header(slot);
                if (previous.SkillContentId != next.SkillContentId ||
                    previous.OwnerOperationIndex != next.OwnerOperationIndex)
                    destinationFrames.ClearPayloads(slot, layout.ProgramIndex(previous.SkillContentId));
            }
            foreach (OrchestrationStateRegion<byte> region in ValidRanges(plan, source))
                source.Slice(region.OffsetBytes, region.SizeBytes).CopyTo(destination.Slice(region.OffsetBytes));
        }
        public static int WriteBlitState<TValue>(OrchestrationPreparedStateLayout<TValue> plan,
            ReadOnlySpan<byte> source, Span<byte> output, bool countOnly = false) where TValue : unmanaged
        {
            int written = 0;
            foreach (OrchestrationStateRegion<byte> region in ValidRanges(plan, source))
            {
                if (!countOnly)
                    source.Slice(region.OffsetBytes, region.SizeBytes).CopyTo(output.Slice(written));
                written += region.SizeBytes;
            }
            return written;
        }

        public static int ReadBlitState<TValue>(OrchestrationPreparedStateLayout<TValue> plan,
            ReadOnlySpan<byte> input, Span<byte> destination) where TValue : unmanaged
        {
            ClearPayloads(plan, destination);
            int read = 0;
            foreach (OrchestrationStateRegion<byte> region in ValidRanges(plan, destination))
            {
                input.Slice(read, region.SizeBytes).CopyTo(destination.Slice(region.OffsetBytes));
                read += region.SizeBytes;
            }
            return read;
        }

        internal static void ClearPayloads<TValue>(OrchestrationPreparedStateLayout<TValue> plan,
            Span<byte> memory) where TValue : unmanaged
        {
            OrchestrationStateValidRanges<TValue>.Enumerator ranges = ValidRanges(plan, memory).GetEnumerator();
            while (ranges.MoveNext())
                if (ranges.IsPayload)
                    memory.Slice(ranges.Current.OffsetBytes, ranges.Current.SizeBytes).Clear();
        }

        public static void AdoptMemory(in OrchestrationStateLayout layout,
            in OrchestrationStateLayout actual, ReadOnlySpan<byte> memory)
        {
            layout.RequireCompatible(in actual);
            if (memory.Length != layout.SizeBytes)
                throw new ArgumentException("BTSMTL 编排区段内存尺寸与已准备布局不匹配。", nameof(memory));
            fixed (byte* address = memory)
                if ((ulong)address % (uint)layout.Alignment != 0)
                    throw new ArgumentException("BTSMTL 编排区段内存地址不满足已准备布局对齐。", nameof(memory));
        }

    }

    public readonly ref struct OrchestrationStateValidRanges<TValue> where TValue : unmanaged
    {
        readonly OrchestrationStateLayoutView m_Plan;
        readonly ReadOnlySpan<byte> m_Memory;

        internal OrchestrationStateValidRanges(OrchestrationStateLayoutView plan,
            ReadOnlySpan<byte> memory)
        {
            m_Plan = plan;
            m_Memory = memory;
        }

        public Enumerator GetEnumerator() => new Enumerator(m_Plan, m_Memory);

        public ref struct Enumerator
        {
            readonly OrchestrationStateLayoutView m_Plan;
            readonly ReadOnlySpan<OrchestrationStateSegment> m_Segments;
            readonly ReadOnlySpan<OrchestrationExecutionFrameState> m_Frames;
            readonly int m_FrameCapacity;
            OrchestrationStateOwnerRecord m_OwnerStorage;
            int m_SegmentIndex;
            int m_FrameSlot;

            internal Enumerator(OrchestrationStateLayoutView plan, ReadOnlySpan<byte> memory)
            {
                m_Plan = plan;
                m_Segments = plan.Segments;
                m_FrameCapacity = plan.FrameCapacity;
                m_OwnerStorage = default;
                OrchestrationStateSegment frames = plan.Segment(OrchestrationStateSegmentRole.Frames);
                m_Frames = SpanReinterpret.Cast<byte, OrchestrationExecutionFrameState>(
                    memory.Slice(frames.OffsetBytes, frames.SizeBytes));
                m_SegmentIndex = -1;
                m_FrameSlot = -1;
                Current = default;
            }

            public OrchestrationStateRegion<byte> Current { get; private set; }
            internal bool IsPayload => m_FrameSlot >= 0;

            public bool MoveNext()
            {
                if (m_FrameSlot < 0)
                {
                    while (++m_SegmentIndex < m_Segments.Length)
                    {
                        OrchestrationStateSegment segment = m_Segments[m_SegmentIndex];
                        if ((!segment.PerFrame || segment.Role == OrchestrationStateSegmentRole.Frames) &&
                            segment.SizeBytes > 0)
                        {
                            Current = new OrchestrationStateRegion<byte>(segment.OffsetBytes, segment.SizeBytes);
                            return true;
                        }
                    }
                    m_FrameSlot = 0;
                    m_SegmentIndex = -1;
                }
                while (m_FrameSlot < m_Frames.Length)
                {
                    ref readonly OrchestrationExecutionFrameState header = ref m_Frames[m_FrameSlot];
                    if (header.SkillContentId != 0)
                    {
                        if (m_SegmentIndex < 0)
                            m_OwnerStorage = m_Plan.OwnerStorage(m_Plan.ProgramIndex(header.SkillContentId),
                                header.OwnerOperationIndex);
                        while (++m_SegmentIndex < m_Segments.Length)
                        {
                            ref readonly OrchestrationStateSegment segment = ref m_Segments[m_SegmentIndex];
                            if (!segment.PerFrame || segment.Role == OrchestrationStateSegmentRole.Frames)
                                continue;
                            OrchestrationStateRegion<byte> region = m_Plan.PayloadRegion(in segment,
                                m_FrameSlot, m_FrameCapacity, in m_OwnerStorage);
                            if (region.SizeBytes > 0)
                            {
                                Current = region;
                                return true;
                            }
                        }
                    }
                    m_FrameSlot++;
                    m_SegmentIndex = -1;
                }
                return false;
            }
        }
    }

    public readonly ref struct OrchestrationCandidateState<TValue> where TValue : unmanaged
    {
        internal OrchestrationCandidateState(OrchestrationExecutionFrames<TValue> frames,
            OrchestrationStateView timeline)
        {
            Frames = frames;
            Timeline = timeline;
        }

        public OrchestrationExecutionFrames<TValue> Frames { get; }
        public OrchestrationStateView Timeline { get; }

        public OrchestrationExecutionCursor<TValue, TNumerics, THost> CreateCursor<TNumerics, THost>(
            OrchestrationProgramView<TValue> program, OrchestrationExecutionScratch<TValue> scratch, int frameSlot,
            ReadOnlySpan<int> characterSlots, in OrchestrationFrameInput<TValue> input,
            OrchestrationIdentityOrdinalView identities, THost host)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            new OrchestrationExecutionCursor<TValue, TNumerics, THost>(program, scratch, Frames, frameSlot,
                characterSlots, Timeline, in input, identities, default, default, host);

        public OrchestrationExecutionCursor<TValue, TNumerics, THost> CreateCursor<TNumerics, THost>(
            OrchestrationProgramView<TValue> program, OrchestrationExecutionSourceBindingView sourceBinding,
            OrchestrationExecutionScratch<TValue> scratch, int frameSlot, OrchestrationExecutionSource<TValue> source,
            ReadOnlySpan<int> characterSlots, in OrchestrationFrameInput<TValue> input,
            OrchestrationIdentityOrdinalView identities, THost host)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            new OrchestrationExecutionCursor<TValue, TNumerics, THost>(program, scratch, Frames, frameSlot,
                characterSlots, Timeline, in input, identities, sourceBinding, source, host);
    }

    public readonly ref struct OrchestrationCommittedState<TValue> where TValue : unmanaged
    {
        internal OrchestrationCommittedState(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            ReadOnlyOrchestrationStateView timeline)
        {
            Frames = frames;
            Timeline = timeline;
        }

        public ReadOnlyOrchestrationExecutionFrames<TValue> Frames { get; }
        public ReadOnlyOrchestrationStateView Timeline { get; }

        public OrchestrationExecutionSource<TValue> BorrowExecutionSource(int frameSlot, ulong tick,
            ReadOnlySpan<OrchestrationStopContext> stopContext) =>
            new OrchestrationExecutionSource<TValue>(Frames, frameSlot, tick, stopContext);
    }
}
