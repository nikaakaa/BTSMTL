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
        readonly OrchestrationExecutionSource<TValue> m_Source;
        readonly int m_CandidateRootFrameSlot;

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
            m_Source = source;
            m_CandidateRootFrameSlot = frameSlot;
        }

        internal void SelectFrame(int frameSlot) => m_FrameSlot = frameSlot;

        internal int BorrowedStateOperationIndex => m_SourceBinding == null ? -1 : m_SourceBinding.SourceStateOperationIndex;
        internal OrchestrationStopContext BorrowedStopContext => m_Source.StopContext;
        internal bool IsBorrowedStateScope(int stateOperationIndex) =>
            m_SourceBinding != null && m_SourceBinding.ContainsSourceState(stateOperationIndex);

        internal ref readonly TValue ReadValue(int valueSlot)
        {
            int characterSlot = m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot];
            if (characterSlot >= 0)
                return ref CharacterValues[characterSlot];
            FrameAddress address = Owner(m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex);
            return ref Values(address)[valueSlot];
        }

        internal ref readonly OrchestrationBlackboardMetadata ReadMetadata(int valueSlot)
        {
            int characterSlot = m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot];
            if (characterSlot >= 0)
                return ref CharacterMetadata[characterSlot];
            FrameAddress address = Owner(m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex);
            return ref Metadata(address)[valueSlot];
        }

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

        internal ref readonly OrchestrationOperationState ReadOperation(int operationIndex)
        {
            FrameAddress address = Owner(m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex);
            ReadOnlySpan<OrchestrationOperationState> operations = address.Source
                ? m_Source.Frames.Operations(address.Index) : m_Frames.Operations(address.Index);
            return ref operations[operationIndex];
        }

        internal ref readonly TValue ReadOutputValue(int operationIndex, int valueSlot)
        {
            FrameAddress address = Owner(m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex);
            return ref Values(address)[valueSlot];
        }

        internal int OperationFrame(int operationIndex) =>
            Owner(m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex).Index;

        internal bool IsBorrowedOperation(int operationIndex) =>
            Owner(m_Program.Operation(operationIndex).ExecutionFrameOwnerOperationIndex).Source;

        internal bool IsBorrowedGraphScope(int graphOwnerOperationIndex)
        {
            FrameAddress owner = Owner(m_Program.Operation(graphOwnerOperationIndex).ExecutionFrameOwnerOperationIndex);
            int frame = m_Source.FrameSlot;
            int operation = m_SourceBinding.SourceOperationIndex;
            while (frame != owner.Index)
            {
                operation = m_Source.Frames.Header(frame).ParentTimelineOperationIndex;
                frame = m_Source.Frames.Header(frame).ParentFrameIndex;
            }
            return m_SourceBinding.ContainsSourceGraph(graphOwnerOperationIndex, operation);
        }

        internal ulong OwnerGeneration(int valueSlot)
        {
            FrameAddress address = Owner(m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex);
            return address.Source ? m_Source.Frames.Header(address.Index).Generation : m_Frames.Header(address.Index).Generation;
        }

        internal ulong FrameTick(int valueSlot, ulong candidateTick) =>
            Owner(m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex).Source ? m_Source.Tick : candidateTick;

        ReadOnlySpan<TValue> CharacterValues => m_SourceBinding == null ? m_CharacterValues : m_Source.CharacterValues;
        ReadOnlySpan<OrchestrationBlackboardMetadata> CharacterMetadata =>
            m_SourceBinding == null ? m_CharacterMetadata : m_Source.CharacterBlackboards;

        ReadOnlySpan<TValue> Values(FrameAddress address) => address.Source
            ? m_Source.Frames.Values(address.Index) : m_Frames.Values(address.Index);

        ReadOnlySpan<OrchestrationBlackboardMetadata> Metadata(FrameAddress address) => address.Source
            ? m_Source.Frames.BlackboardMetadata(address.Index) : m_Frames.BlackboardMetadata(address.Index);

        FrameAddress Owner(int ownerOperationIndex)
        {
            if (m_SourceBinding == null)
                return new FrameAddress(false, m_Frames.OwnerFrame(m_FrameSlot, ownerOperationIndex));
            int frame = m_FrameSlot;
            while (m_Frames.Header(frame).OwnerOperationIndex != ownerOperationIndex && frame != m_CandidateRootFrameSlot)
                frame = m_Frames.Header(frame).ParentFrameIndex;
            return m_Frames.Header(frame).OwnerOperationIndex == ownerOperationIndex
                ? new FrameAddress(false, frame)
                : new FrameAddress(true, m_Source.Frames.OwnerFrame(m_Source.FrameSlot, ownerOperationIndex));
        }

        readonly struct FrameAddress
        {
            internal FrameAddress(bool source, int index) { Source = source; Index = index; }
            internal bool Source { get; }
            internal int Index { get; }
        }
    }
}
