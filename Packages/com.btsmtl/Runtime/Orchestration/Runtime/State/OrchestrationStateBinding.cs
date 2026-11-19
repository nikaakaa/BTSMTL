using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public static unsafe class OrchestrationStateBinding
    {
        public static void RetireFrameOverrides<TValue>(ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<int> skillContentIds, OrchestrationValueOverrides<TValue> currentOverrides,
            ulong tick) where TValue : unmanaged
        {
            // 宿主只对当前输入状态更代；冻结来源快照保留其原 tick。
            for (int index = 0; index < currentOverrides.Length; index++)
            {
                ref OrchestrationValueOverride<TValue> value = ref currentOverrides[index];
                if (value.Mode == OrchestrationValueOverrideMode.None ||
                    value.Address.Kind != OrchestrationValueOverrideKind.Blackboard ||
                    value.Address.Owner.InvocationGeneration == tick)
                    continue;
                for (int programIndex = 0; programIndex < programs.Length; programIndex++)
                {
                    if (value.Address.Owner.SkillContentId == skillContentIds[programIndex] &&
                        programs[programIndex].Blackboard(value.Address.Slot).ScopeKind == OrchestrationScopeKind.Frame)
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
            ReadOnlySpan<byte> source, Span<byte> destination) where TValue : unmanaged =>
            CopyMemory<TValue>(plan.View, source, destination);

        public static void CopyMemory<TValue>(OrchestrationStateLayoutView layout,
            ReadOnlySpan<byte> source, Span<byte> destination) where TValue : unmanaged
        {
            var sourceFrames = new ReadOnlyOrchestrationExecutionFrames<TValue>(layout, source);
            var destinationFrames = new OrchestrationExecutionFrames<TValue>(layout, destination);
            for (int slot = 0, sourceRemaining = sourceFrames.Count, destinationRemaining = destinationFrames.Count;
                 sourceRemaining > 0 || destinationRemaining > 0; slot++)
            {
                ref OrchestrationExecutionFrameState previous = ref destinationFrames.Header(slot);
                ref readonly OrchestrationExecutionFrameState next = ref sourceFrames.Header(slot);
                if (previous.SkillContentId != 0)
                {
                    destinationRemaining--;
                    if (previous.SkillContentId != next.SkillContentId ||
                        previous.OwnerOperationIndex != next.OwnerOperationIndex)
                        destinationFrames.ClearPayloads(slot,
                            layout.PayloadSizesForSkill(previous.SkillContentId, previous.OwnerOperationIndex));
                }
                if (next.SkillContentId != 0)
                {
                    sourceRemaining--;
                    previous = next;
                    var payloads = layout.PayloadRanges(slot,
                        layout.PayloadSizesForSkill(next.SkillContentId, next.OwnerOperationIndex));
                    while (payloads.MoveNext(out var region))
                        source.Slice(region.OffsetBytes, region.SizeBytes).CopyTo(destination.Slice(region.OffsetBytes));
                }
                else if (previous.SkillContentId != 0)
                    previous = default;
            }
            foreach (OrchestrationStateSegment segment in layout.Segments)
                if (!segment.PerFrame && segment.Role != OrchestrationStateSegmentRole.Frames && segment.SizeBytes > 0)
                    source.Slice(segment.OffsetBytes, segment.SizeBytes).CopyTo(destination.Slice(segment.OffsetBytes));
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

        public Enumerator GetEnumerator() =>
            new Enumerator(m_Plan, new ReadOnlyOrchestrationExecutionFrames<TValue>(m_Plan, m_Memory));

        public ref struct Enumerator
        {
            readonly ReadOnlySpan<OrchestrationStateSegment> m_Segments;
            readonly ReadOnlyOrchestrationExecutionFrames<TValue> m_Frames;
            OrchestrationFramePayloadRanges m_Payloads;
            int m_SegmentIndex;
            int m_FrameSlot;
            int m_RemainingFrames;

            internal Enumerator(OrchestrationStateLayoutView plan, ReadOnlyOrchestrationExecutionFrames<TValue> frames)
            {
                m_Segments = plan.Segments;
                m_Frames = frames;
                m_Payloads = default;
                m_SegmentIndex = -1;
                m_FrameSlot = -1;
                m_RemainingFrames = 0;
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
                    // 读回时固定区段先恢复帧头和在用帧数量。
                    m_RemainingFrames = m_Frames.Count;
                    if (m_RemainingFrames > 0)
                        BindNextPayloadFrame();
                }
                while (m_RemainingFrames > 0)
                {
                    if (m_Payloads.MoveNext(out var region))
                    {
                        Current = region;
                        return true;
                    }
                    m_FrameSlot++;
                    if (--m_RemainingFrames > 0)
                        BindNextPayloadFrame();
                }
                return false;
            }

            void BindNextPayloadFrame()
            {
                ref readonly OrchestrationExecutionFrameState header = ref m_Frames.Header(m_FrameSlot);
                while (header.SkillContentId == 0)
                    header = ref m_Frames.Header(++m_FrameSlot);
                OrchestrationStateLayoutView plan = m_Frames.Plan;
                m_Payloads = plan.PayloadRanges(m_FrameSlot,
                    plan.PayloadSizesForSkill(header.SkillContentId, header.OwnerOperationIndex));
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
            int programIndex, OrchestrationProgramView<TValue> program, OrchestrationExecutionScratch<TValue> scratch, int frameSlot,
            ReadOnlySpan<int> characterSlots, in OrchestrationFrameInput<TValue> input,
            OrchestrationIdentityOrdinalView identities, THost host)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            new OrchestrationExecutionCursor<TValue, TNumerics, THost>(programIndex, program, scratch, Frames, frameSlot,
                characterSlots, Timeline, in input, identities, default, default, host);

        public OrchestrationExecutionCursor<TValue, TNumerics, THost> CreateCursor<TNumerics, THost>(
            int programIndex, OrchestrationProgramView<TValue> program, OrchestrationExecutionSourceBindingView sourceBinding,
            OrchestrationExecutionScratch<TValue> scratch, int frameSlot, OrchestrationExecutionSource<TValue> source,
            ReadOnlySpan<int> characterSlots, in OrchestrationFrameInput<TValue> input,
            OrchestrationIdentityOrdinalView identities, THost host)
            where TNumerics : struct, IOrchestrationNumerics<TValue>
            where THost : struct, IOrchestrationHost<TValue> =>
            new OrchestrationExecutionCursor<TValue, TNumerics, THost>(programIndex, program, scratch, Frames, frameSlot,
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
