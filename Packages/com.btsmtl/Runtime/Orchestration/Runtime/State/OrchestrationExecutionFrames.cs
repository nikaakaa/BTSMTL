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

        public static OrchestrationExecutionStateCapacity PrepareCapacity(
            ReadOnlySpan<OrchestrationProgram<TValue>> programs, int frameCount)
        {
            int operations = 0;
            int values = 0;
            for (int i = 0; i < programs.Length; i++)
            {
                operations = Math.Max(operations, programs[i].OperationCount);
                values = Math.Max(values, programs[i].ValueSlotCount);
            }
            return new OrchestrationExecutionStateCapacity(frameCount, operations, values);
        }

        public OrchestrationExecutionFrames(
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
            int slot = InsertionSlot(actionInstanceId);
            return slot < Count && m_Frames[slot].ActionInstanceId == actionInstanceId
                ? slot : -1;
        }

        public int FindPersistent(int skillContentId)
        {
            for (int slot = 0; slot < Count && m_Frames[slot].ActionInstanceId == 0; slot++)
                if (m_Frames[slot].SkillContentId == skillContentId)
                    return slot;
            return -1;
        }

        public int Insert(OrchestrationExecutionFrameState frame, ReadOnlySpan<TValue> defaultValues)
        {
            int count = Count;
            int slot = InsertionSlot(frame.ActionInstanceId);
            Move(slot, slot + 1, count - slot);
            m_Frames[slot] = frame;
            Operations(slot).Clear();
            BlackboardMetadata(slot).Clear();
            Span<TValue> values = Values(slot);
            defaultValues.CopyTo(values);
            values.Slice(defaultValues.Length).Clear();
            m_Roster[0].Count = count + 1;
            return slot;
        }

        public void RemoveAt(int frameSlot)
        {
            int count = Count - 1;
            Move(frameSlot + 1, frameSlot, count - frameSlot);
            m_Frames[count] = default;
            Operations(count).Clear();
            Values(count).Clear();
            BlackboardMetadata(count).Clear();
            m_Roster[0].Count = count;
        }

        int InsertionSlot(ulong actionInstanceId)
        {
            int first = 0;
            int last = Count;
            while (first < last)
            {
                int middle = first + ((last - first) >> 1);
                if (m_Frames[middle].ActionInstanceId < actionInstanceId)
                    first = middle + 1;
                else
                    last = middle;
            }
            return first;
        }

        void Move(int sourceFrame, int destinationFrame, int frameCount)
        {
            m_Frames.Slice(sourceFrame, frameCount)
                .CopyTo(m_Frames.Slice(destinationFrame, frameCount));
            m_Operations.Slice(sourceFrame * m_OperationsPerFrame,
                    frameCount * m_OperationsPerFrame)
                .CopyTo(m_Operations.Slice(destinationFrame * m_OperationsPerFrame,
                    frameCount * m_OperationsPerFrame));
            m_Values.Slice(sourceFrame * m_ValuesPerFrame, frameCount * m_ValuesPerFrame)
                .CopyTo(m_Values.Slice(destinationFrame * m_ValuesPerFrame,
                    frameCount * m_ValuesPerFrame));
            m_BlackboardMetadata.Slice(sourceFrame * m_BlackboardMetadataPerFrame,
                    frameCount * m_BlackboardMetadataPerFrame)
                .CopyTo(m_BlackboardMetadata.Slice(destinationFrame * m_BlackboardMetadataPerFrame,
                    frameCount * m_BlackboardMetadataPerFrame));
        }
    }
}
