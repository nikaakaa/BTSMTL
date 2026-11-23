using System;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public readonly ref struct ReadOnlyOrchestrationExecutionFrames<TValue> where TValue : unmanaged
    {
        readonly ReadOnlySpan<OrchestrationExecutionFrameState> m_Frames;
        readonly ReadOnlySpan<OrchestrationExecutionRosterState> m_Roster;
        readonly ReadOnlySpan<byte> m_Memory;

        internal ReadOnlyOrchestrationExecutionFrames(OrchestrationStateLayoutView plan,
            ReadOnlySpan<byte> memory)
        {
            Plan = plan;
            m_Memory = memory;
            OrchestrationStateSegment frames = plan.Segment(OrchestrationStateSegmentRole.Frames);
            m_Frames = SpanReinterpret.Cast<byte, OrchestrationExecutionFrameState>(
                memory.Slice(frames.OffsetBytes, frames.SizeBytes));
            OrchestrationStateSegment roster = plan.Segment(OrchestrationStateSegmentRole.Roster);
            m_Roster = SpanReinterpret.Cast<byte, OrchestrationExecutionRosterState>(
                memory.Slice(roster.OffsetBytes, roster.SizeBytes));
        }

        internal ReadOnlyOrchestrationExecutionFrames(OrchestrationStateLayoutView plan, ReadOnlySpan<byte> memory,
            ReadOnlySpan<OrchestrationExecutionFrameState> frames, ReadOnlySpan<OrchestrationExecutionRosterState> roster)
        {
            Plan = plan;
            m_Memory = memory;
            m_Frames = frames;
            m_Roster = roster;
        }

        public OrchestrationStateLayoutView Plan { get; }
        public ReadOnlySpan<byte> Memory => m_Memory;
        public int Count => m_Roster[0].Count;
        public int Capacity => m_Frames.Length;
        public ref readonly OrchestrationExecutionFrameState Header(int frameSlot) => ref m_Frames[frameSlot];
        public ReadOnlySpan<OrchestrationExecutionFrameState> HeaderSpan(int frameSlot) => m_Frames.Slice(frameSlot, 1);
        public ReadOnlySpan<OrchestrationOperationState> Operations(int frameSlot) =>
            Payload<OrchestrationOperationState>(frameSlot, OrchestrationStateSegmentRole.Operations,
                Plan.ProgramIndex(m_Frames[frameSlot].SkillContentId));
        public ref readonly OrchestrationOperationState Operation(int frameSlot, int operationIndex)
        {
            int programIndex = Plan.ProgramIndex(m_Frames[frameSlot].SkillContentId);
            return ref Element<OrchestrationOperationState>(frameSlot, OrchestrationStateSegmentRole.Operations,
                Plan.ProgramStorage(programIndex).OperationIndices[operationIndex]);
        }
        public ReadOnlySpan<TValue> SavedOutputs(int frameSlot) =>
            Payload<TValue>(frameSlot, OrchestrationStateSegmentRole.SavedOutputs,
                Plan.ProgramIndex(m_Frames[frameSlot].SkillContentId));
        public ReadOnlySpan<OrchestrationBlackboardMetadata> BlackboardMetadata(int frameSlot) =>
            Payload<OrchestrationBlackboardMetadata>(frameSlot, OrchestrationStateSegmentRole.PrivateMetadata,
                Plan.ProgramIndex(m_Frames[frameSlot].SkillContentId));

        public ReadOnlySpan<OrchestrationBlackboardMetadata> CharacterMetadata
        {
            get
            {
                OrchestrationStateSegment segment = Plan.Segment(OrchestrationStateSegmentRole.CharacterMetadata);
                return SpanReinterpret.Cast<byte, OrchestrationBlackboardMetadata>(
                    m_Memory.Slice(segment.OffsetBytes, segment.SizeBytes));
            }
        }

        internal ReadOnlySpan<T> Payload<T>(int frameSlot, OrchestrationStateSegmentRole role,
            int programIndex) where T : unmanaged
        {
            OrchestrationStateRegion<byte> region = Plan.PayloadRegion(frameSlot, programIndex,
                m_Frames[frameSlot].OwnerOperationIndex, role);
            return SpanReinterpret.Cast<byte, T>(m_Memory.Slice(region.OffsetBytes, region.SizeBytes));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ref readonly T Element<T>(int frameSlot, OrchestrationStateSegmentRole role,
            int index) where T : unmanaged =>
            ref ElementAt<T>(Plan.PayloadOffset(frameSlot, role), index);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe ref readonly T ElementAt<T>(int payloadOffset, int index) where T : unmanaged =>
            ref SpanReinterpret.Cast<byte, T>(m_Memory.Slice(
                payloadOffset + index * sizeof(T), sizeof(T)))[0];

        public int FindTimelineSource(int skillContentId, int ownerOperationIndex, int operationIndex,
            in OrchestrationTimelineStart<OrchestrationTime> start)
        {
            int programIndex = Plan.ProgramIndex(skillContentId);
            OrchestrationFramePoolRange pool = Plan.FramePool(programIndex, ownerOperationIndex);
            int timeline = Plan.OperationStateIndices(programIndex)[operationIndex].Timeline;
            int end = pool.First + pool.Count;
            for (int slot = pool.First; slot < end; slot++)
            {
                ref readonly OrchestrationExecutionFrameState frame = ref m_Frames[slot];
                if (frame.SkillContentId == skillContentId && frame.OwnerOperationIndex == ownerOperationIndex &&
                    frame.ActionInstanceId == start.OwnerInstanceId &&
                    Element<ulong>(slot, OrchestrationStateSegmentRole.TimelineHandles,
                        timeline) == start.PlaybackIdentity)
                    return slot;
            }
            return -1;
        }

        public int OwnerFrame(int frameSlot, int ownerOperationIndex)
        {
            while (m_Frames[frameSlot].OwnerOperationIndex != ownerOperationIndex)
                frameSlot = m_Frames[frameSlot].ParentFrameIndex;
            return frameSlot;
        }

        public int ActionFrame(int frameSlot)
        {
            ulong action = m_Frames[frameSlot].ActionInstanceId;
            while (m_Frames[frameSlot].ParentFrameIndex >= 0 &&
                m_Frames[m_Frames[frameSlot].ParentFrameIndex].ActionInstanceId == action)
                frameSlot = m_Frames[frameSlot].ParentFrameIndex;
            return frameSlot;
        }
    }
}
