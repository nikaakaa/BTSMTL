using System;

namespace BTSMTL.Orchestration
{
    internal ref struct OrchestrationBlackboardStorageView<TValue, TStorage>
        where TValue : unmanaged
        where TStorage : struct, IOrchestrationValueStorage<TValue>
    {
        readonly OrchestrationProgram<TValue> m_Program;
        readonly OrchestrationExecutionFrames<TValue> m_Frames;
        readonly int m_ProgramIndex;
        int m_FrameSlot;
        readonly ReadOnlySpan<int> m_CharacterSlots;
        readonly OrchestrationExecutionSourceBinding<TValue> m_SourceBinding;
        OrchestrationExecutionSourceReader<TValue> m_Reader;

        internal OrchestrationBlackboardStorageView(OrchestrationProgram<TValue> program,
            OrchestrationExecutionFrames<TValue> frames, int frameSlot, ReadOnlySpan<int> characterSlots,
            OrchestrationExecutionSourceBinding<TValue> sourceBinding, OrchestrationExecutionSource<TValue> source)
        {
            m_Program = program;
            m_Frames = frames;
            m_ProgramIndex = frames.Plan.ProgramIndex(frames.Header(frameSlot).SkillContentId);
            m_FrameSlot = frameSlot;
            m_CharacterSlots = characterSlots;
            m_SourceBinding = sourceBinding;
            m_Reader = new OrchestrationExecutionSourceReader<TValue>(frames.AsReadOnly(), frameSlot,
                m_ProgramIndex, sourceBinding != null, source);
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

        internal void ReadValue(int valueSlot, ref TValue destination) =>
            m_Reader.ReadValue<TStorage>(valueSlot, m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex,
                m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot], ref destination);

        internal ref readonly OrchestrationBlackboardMetadata ReadMetadata(int valueSlot) =>
            ref m_Reader.ReadMetadata(valueSlot, m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex,
                m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot]);

        internal void WriteValue(int valueSlot, in TValue value)
        {
            int characterSlot = m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot];
            if (characterSlot >= 0)
            {
                OrchestrationValueAddress address = m_Frames.Plan.CharacterAddress(characterSlot);
                default(TStorage).Write(m_Frames.Memory, in address, in value);
            }
            else
            {
                int frameSlot = m_Frames.OwnerFrame(m_FrameSlot,
                    m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex);
                OrchestrationValueAddress address = m_Frames.Plan.PrivateAddress(m_ProgramIndex, frameSlot, valueSlot);
                default(TStorage).Write(m_Frames.Memory, in address, in value);
            }
        }

        internal ref OrchestrationBlackboardMetadata WriteMetadata(int valueSlot)
        {
            int characterSlot = m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot];
            if (characterSlot >= 0)
                return ref m_Frames.CharacterMetadata[characterSlot];
            int frameSlot = m_Frames.OwnerFrame(m_FrameSlot,
                m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex);
            return ref m_Frames.Payload<OrchestrationBlackboardMetadata>(frameSlot,
                OrchestrationStateSegmentRole.PrivateMetadata, m_ProgramIndex)[
                m_Program.ExecutionStorage.Values.Span[valueSlot].MetadataIndex];
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
