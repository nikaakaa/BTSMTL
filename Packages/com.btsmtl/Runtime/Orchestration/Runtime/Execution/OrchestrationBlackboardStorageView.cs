using System;

namespace BTSMTL.Orchestration
{
    internal ref struct OrchestrationBlackboardStorageView<TValue>
        where TValue : unmanaged
    {
        readonly OrchestrationProgram<TValue> m_Program;
        readonly OrchestrationExecutionFrames<TValue> m_Frames;
        int m_FrameSlot;
        readonly Span<TValue> m_CharacterValues;
        readonly Span<OrchestrationBlackboardMetadata> m_CharacterMetadata;
        readonly ReadOnlySpan<int> m_CharacterSlots;
        readonly OrchestrationExecutionSourceBinding<TValue> m_SourceBinding;
        OrchestrationExecutionSourceReader<TValue> m_Reader;

        internal OrchestrationBlackboardStorageView(OrchestrationProgram<TValue> program,
            OrchestrationExecutionFrames<TValue> frames, int frameSlot, Span<TValue> characterValues,
            Span<OrchestrationBlackboardMetadata> characterMetadata, ReadOnlySpan<int> characterSlots,
            OrchestrationExecutionSourceBinding<TValue> sourceBinding, OrchestrationExecutionSource<TValue> source)
        {
            m_Program = program;
            m_Frames = frames;
            m_FrameSlot = frameSlot;
            m_CharacterValues = characterValues;
            m_CharacterMetadata = characterMetadata;
            m_CharacterSlots = characterSlots;
            m_SourceBinding = sourceBinding;
            m_Reader = new OrchestrationExecutionSourceReader<TValue>(frames.AsReadOnly(), frameSlot,
                characterValues, characterMetadata, sourceBinding != null, source);
        }

        internal void SelectFrame(int frameSlot)
        {
            m_FrameSlot = frameSlot;
            m_Reader.SelectFrame(frameSlot);
        }

        internal OrchestrationExecutionSourceReader<TValue> Reader => m_Reader;
        internal int CharacterSlot(int valueSlot) =>
            m_CharacterSlots.IsEmpty ? valueSlot : m_CharacterSlots[valueSlot];
        internal int BorrowedStateOperationIndex => m_SourceBinding == null ? -1 : m_SourceBinding.SourceStateOperationIndex;
        internal OrchestrationStopContext BorrowedStopContext => m_Reader.SourceStopContext;
        internal bool IsBorrowedStateScope(int stateOperationIndex) =>
            m_SourceBinding != null && m_SourceBinding.ContainsSourceState(stateOperationIndex);

        internal ref readonly TValue ReadValue(int valueSlot) =>
            ref m_Reader.ReadValue(valueSlot, m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex,
                m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot]);

        internal ref readonly OrchestrationBlackboardMetadata ReadMetadata(int valueSlot) =>
            ref m_Reader.ReadMetadata(valueSlot, m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex,
                m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot]);

        internal ref TValue WriteValue(int valueSlot)
        {
            int characterSlot = m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot];
            if (characterSlot >= 0)
                return ref m_CharacterValues[characterSlot];
            return ref m_Frames.Values(m_Frames.OwnerFrame(m_FrameSlot,
                m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex))[valueSlot];
        }

        internal ref OrchestrationBlackboardMetadata WriteMetadata(int valueSlot)
        {
            int characterSlot = m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot];
            if (characterSlot >= 0)
                return ref m_CharacterMetadata[characterSlot];
            return ref m_Frames.BlackboardMetadata(m_Frames.OwnerFrame(m_FrameSlot,
                m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex))[valueSlot];
        }

        internal ref readonly OrchestrationOperationState ReadOperation(int operationIndex) =>
            ref m_Reader.ReadOperation(operationIndex, m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex);

        internal ReadOnlySpan<OrchestrationExecutionFrameState> ReadExecutionFrame(int operationIndex) =>
            m_Reader.ReadExecutionFrame(m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex);

        internal ref readonly TValue ReadOutputValue(int operationIndex, int valueSlot) =>
            ref m_Reader.ReadOutputValue(valueSlot, m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex);

        internal int OperationFrame(int operationIndex) =>
            m_Reader.OwnerFrameIndex(m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex);

        internal bool IsBorrowedOperation(int operationIndex) =>
            m_Reader.IsBorrowedOwner(m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex);

        internal bool IsBorrowedGraphScope(int graphOwnerOperationIndex)
        {
            int ownerFrame = m_Reader.OwnerFrameIndex(m_Program.Operation(graphOwnerOperationIndex).ExecutionFrameOwnerOperationIndex);
            int frame = m_Reader.SourceFrameIndex;
            int operation = m_SourceBinding.SourceOperationIndex;
            while (frame != ownerFrame)
            {
                ref readonly OrchestrationExecutionFrameState header = ref m_Reader.SourceHeader(frame);
                operation = header.ParentTimelineOperationIndex;
                frame = header.ParentFrameIndex;
            }
            return m_SourceBinding.ContainsSourceGraph(graphOwnerOperationIndex, operation);
        }

        internal ulong OwnerGeneration(int valueSlot) =>
            m_Reader.OwnerGeneration(m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex);

        internal ulong FrameTick(int valueSlot, ulong candidateTick) =>
            m_Reader.FrameTick(m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex, candidateTick);
    }
}
