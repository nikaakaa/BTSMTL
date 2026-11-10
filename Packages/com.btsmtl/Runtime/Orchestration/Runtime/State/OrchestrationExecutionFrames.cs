using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public readonly ref struct OrchestrationExecutionFrames<TValue> where TValue : unmanaged
    {
        readonly Span<OrchestrationExecutionFrameState> m_Frames;
        readonly Span<OrchestrationExecutionRosterState> m_Roster;
        readonly Span<byte> m_Memory;

        public static OrchestrationExecutionCapacity PrepareCapacity(
            ReadOnlySpan<OrchestrationProgram<TValue>> programs,
            ReadOnlySpan<OrchestrationRootAdmission> admissions,
            ReadOnlySpan<OrchestrationTimelineCallCapacity> calls) =>
            OrchestrationExecutionCapacity.Prepare(programs, admissions, calls);

        internal OrchestrationExecutionFrames(OrchestrationStateLayoutView plan,
            Span<byte> memory)
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
        public Span<byte> Memory => m_Memory;
        public int Count => m_Roster[0].Count;
        public int Capacity => m_Frames.Length;

        public ReadOnlyOrchestrationExecutionFrames<TValue> AsReadOnly() =>
            new ReadOnlyOrchestrationExecutionFrames<TValue>(Plan, m_Memory);

        public ref OrchestrationExecutionFrameState Header(int frameSlot) =>
            ref m_Frames[frameSlot];

        internal ReadOnlySpan<OrchestrationExecutionFrameState> HeaderSpan(int frameSlot) =>
            m_Frames.Slice(frameSlot, 1);

        public Span<OrchestrationOperationState> Operations(int frameSlot) =>
            Payload<OrchestrationOperationState>(frameSlot, OrchestrationStateSegmentRole.Operations,
                Plan.ProgramIndex(m_Frames[frameSlot].SkillContentId));

        public ref OrchestrationOperationState Operation(int frameSlot, int operationIndex)
        {
            int programIndex = Plan.ProgramIndex(m_Frames[frameSlot].SkillContentId);
            return ref Payload<OrchestrationOperationState>(frameSlot, OrchestrationStateSegmentRole.Operations,
                programIndex)[Plan.ProgramStorage(programIndex).OperationIndices[operationIndex]];
        }

        public Span<TValue> SavedOutputs(int frameSlot) =>
            Payload<TValue>(frameSlot, OrchestrationStateSegmentRole.SavedOutputs,
                Plan.ProgramIndex(m_Frames[frameSlot].SkillContentId));

        public Span<OrchestrationBlackboardMetadata> BlackboardMetadata(int frameSlot) =>
            Payload<OrchestrationBlackboardMetadata>(frameSlot, OrchestrationStateSegmentRole.PrivateMetadata,
                Plan.ProgramIndex(m_Frames[frameSlot].SkillContentId));

        public Span<OrchestrationBlackboardMetadata> CharacterMetadata
        {
            get
            {
                OrchestrationStateSegment segment = Plan.Segment(OrchestrationStateSegmentRole.CharacterMetadata);
                return SpanReinterpret.Cast<byte, OrchestrationBlackboardMetadata>(
                    m_Memory.Slice(segment.OffsetBytes, segment.SizeBytes));
            }
        }

        internal Span<T> Payload<T>(int frameSlot, OrchestrationStateSegmentRole role,
            int programIndex) where T : unmanaged
        {
            OrchestrationStateRegion<byte> region = Plan.PayloadRegion(frameSlot, programIndex,
                m_Frames[frameSlot].OwnerOperationIndex, role);
            return SpanReinterpret.Cast<byte, T>(m_Memory.Slice(region.OffsetBytes, region.SizeBytes));
        }

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

        public int ActionFrame(int frameSlot)
        {
            ulong action = m_Frames[frameSlot].ActionInstanceId;
            while (m_Frames[frameSlot].ParentFrameIndex >= 0 &&
                m_Frames[m_Frames[frameSlot].ParentFrameIndex].ActionInstanceId == action)
                frameSlot = m_Frames[frameSlot].ParentFrameIndex;
            return frameSlot;
        }

        public int Insert<TStorage>(OrchestrationExecutionFrameState frame, ReadOnlySpan<TValue> defaultValues)
            where TStorage : struct, IOrchestrationValueStorage<TValue>
        {
            int slot = 0;
            while (m_Frames[slot].SkillContentId != 0)
                slot++;
            m_Frames[slot] = frame;
            int programIndex = Plan.ProgramIndex(frame.SkillContentId);
            ClearPayloads(slot, Plan.PayloadSizes(programIndex, frame.OwnerOperationIndex));
            ReadOnlySpan<OrchestrationValueStorageSlot> values = Plan.ProgramStorage(programIndex).Values;
            Span<TValue> outputs = Payload<TValue>(slot, OrchestrationStateSegmentRole.SavedOutputs, programIndex);
            for (int valueSlot = 0; valueSlot < values.Length; valueSlot++)
            {
                OrchestrationValueStorageSlot storage = values[valueSlot];
                if (storage.OwnerOperationIndex != frame.OwnerOperationIndex)
                    continue;
                if (storage.ValueIndex >= 0)
                {
                    OrchestrationValueAddress address = Plan.PrivateAddress(programIndex, slot, valueSlot);
                    default(TStorage).Write(m_Memory, in address, in defaultValues[valueSlot]);
                }
                if (storage.SavedOutputIndex >= 0)
                    outputs[storage.SavedOutputIndex] = defaultValues[valueSlot];
            }
            m_Roster[0].Count++;
            return slot;
        }

        public void RemoveAt(int frameSlot)
        {
            ref readonly OrchestrationExecutionFrameState frame = ref m_Frames[frameSlot];
            ClearPayloads(frameSlot, Plan.PayloadSizesForSkill(frame.SkillContentId, frame.OwnerOperationIndex));
            m_Frames[frameSlot] = default;
            m_Roster[0].Count--;
        }

        internal void ClearPayloads(int frameSlot, ReadOnlySpan<int> sizes)
        {
            ReadOnlySpan<OrchestrationFramePayloadSegment> payloads = Plan.FramePayloads;
            for (int i = 0; i < sizes.Length; i++)
            {
                int size = sizes[i];
                if (size > 0)
                {
                    OrchestrationStateRegion<byte> region = payloads[i].AtFrame(frameSlot, size);
                    m_Memory.Slice(region.OffsetBytes, region.SizeBytes).Clear();
                }
            }
        }
    }
}
