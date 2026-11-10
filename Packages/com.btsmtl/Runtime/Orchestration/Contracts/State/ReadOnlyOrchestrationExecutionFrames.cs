using System;
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
            return ref Payload<OrchestrationOperationState>(frameSlot, OrchestrationStateSegmentRole.Operations,
                programIndex)[Plan.ProgramStorage(programIndex).OperationIndices[operationIndex]];
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
