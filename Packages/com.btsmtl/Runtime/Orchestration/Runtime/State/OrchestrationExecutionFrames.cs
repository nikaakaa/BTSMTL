using System;
using System.Runtime.InteropServices;

namespace BTSMTL.Orchestration
{
    public ref struct OrchestrationExecutionFrames<TValue> where TValue : unmanaged
    {
        readonly Span<OrchestrationExecutionFrameState> m_Frames;
        readonly Span<OrchestrationExecutionRosterState> m_Roster;
        readonly Span<OrchestrationOperationState> m_Operations;
        readonly Span<TValue> m_Values;
        readonly Span<OrchestrationBlackboardMetadata> m_BlackboardMetadata;
        readonly int m_OperationsPerFrame;
        readonly int m_ValuesPerFrame;
        readonly int m_BlackboardMetadataPerFrame;

        public static OrchestrationExecutionCapacity PrepareCapacity(
            ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<OrchestrationRootAdmission> admissions,
            ReadOnlySpan<OrchestrationTimelineCallCapacity> calls) =>
            OrchestrationExecutionCapacity.Prepare(programs, admissions, calls);

        internal OrchestrationExecutionFrames(
            Span<OrchestrationExecutionFrameState> frames,
            ref OrchestrationExecutionRosterState roster,
            Span<OrchestrationOperationState> operations,
            Span<TValue> values,
            Span<OrchestrationBlackboardMetadata> blackboardMetadata)
        {
            m_Frames = frames;
            m_Roster = MemoryMarshal.CreateSpan(ref roster, 1);
            m_Operations = operations;
            m_Values = values;
            m_BlackboardMetadata = blackboardMetadata;
            m_OperationsPerFrame = operations.Length / frames.Length;
            m_ValuesPerFrame = values.Length / frames.Length;
            m_BlackboardMetadataPerFrame = blackboardMetadata.Length / frames.Length;
        }

        public int Count => m_Roster[0].Count;
        public int Capacity => m_Frames.Length;

        public ref OrchestrationExecutionFrameState Header(int frameSlot) =>
            ref m_Frames[frameSlot];

        public Span<OrchestrationOperationState> Operations(int frameSlot) =>
            m_Operations.Slice(frameSlot * m_OperationsPerFrame, m_OperationsPerFrame);

        public Span<TValue> Values(int frameSlot) =>
            m_Values.Slice(frameSlot * m_ValuesPerFrame, m_ValuesPerFrame);

        public Span<OrchestrationBlackboardMetadata> BlackboardMetadata(int frameSlot) =>
            m_BlackboardMetadata.Slice(frameSlot * m_BlackboardMetadataPerFrame,
                m_BlackboardMetadataPerFrame);

        public int Find(ulong actionInstanceId)
        {
            for (int slot = 0; slot < m_Frames.Length; slot++)
                if (m_Frames[slot].SkillContentId != 0 && m_Frames[slot].TimelineStart.PlaybackIdentity == 0 &&
                    m_Frames[slot].ActionInstanceId == actionInstanceId)
                    return slot;
            return -1;
        }

        public int FindPersistent(int skillContentId)
        {
            for (int slot = 0; slot < m_Frames.Length; slot++)
                if (m_Frames[slot].TimelineStart.PlaybackIdentity == 0 && m_Frames[slot].ActionInstanceId == 0 &&
                    m_Frames[slot].SkillContentId == skillContentId)
                    return slot;
            return -1;
        }

        public int FindTimeline(in OrchestrationTimelineStart<OrchestrationTime> start,
            in OrchestrationTimelineInvocationToken token)
        {
            for (int slot = 0; slot < m_Frames.Length; slot++)
                if (m_Frames[slot].SkillContentId != 0 &&
                    m_Frames[slot].TimelineStart.Equals(start) &&
                    m_Frames[slot].ClipOrdinal == token.ClipOrdinal &&
                    m_Frames[slot].Cycle == token.Cycle && m_Frames[slot].Marker == token.Marker)
                    return slot;
            return -1;
        }

        public int OwnerFrame(int frameSlot, int ownerOperationIndex)
        {
            while (m_Frames[frameSlot].OwnerOperationIndex != ownerOperationIndex)
                frameSlot = m_Frames[frameSlot].ParentFrameIndex;
            return frameSlot;
        }

        public int Insert(OrchestrationExecutionFrameState frame, ReadOnlySpan<TValue> defaultValues)
        {
            int slot = 0;
            while (m_Frames[slot].SkillContentId != 0)
                slot++;
            m_Frames[slot] = frame;
            Operations(slot).Clear();
            BlackboardMetadata(slot).Clear();
            Span<TValue> values = Values(slot);
            defaultValues.CopyTo(values);
            values.Slice(defaultValues.Length).Clear();
            m_Roster[0].Count++;
            return slot;
        }

        public void RemoveAt(int frameSlot)
        {
            m_Frames[frameSlot] = default;
            Operations(frameSlot).Clear();
            Values(frameSlot).Clear();
            BlackboardMetadata(frameSlot).Clear();
            m_Roster[0].Count--;
        }
    }
}
