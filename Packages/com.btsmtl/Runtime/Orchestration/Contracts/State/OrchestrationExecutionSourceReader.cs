using System;

namespace BTSMTL.Orchestration
{
    public ref struct OrchestrationExecutionSourceReader<TValue> where TValue : unmanaged
    {
        readonly ReadOnlyOrchestrationExecutionFrames<TValue> m_Frames;
        int m_FrameSlot;
        readonly ReadOnlySpan<TValue> m_CharacterValues;
        readonly ReadOnlySpan<OrchestrationBlackboardMetadata> m_CharacterMetadata;
        readonly bool m_HasSource;
        readonly OrchestrationExecutionSource<TValue> m_Source;
        readonly int m_CandidateRootFrameSlot;

        internal OrchestrationExecutionSourceReader(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            int frameSlot, ReadOnlySpan<TValue> characterValues,
            ReadOnlySpan<OrchestrationBlackboardMetadata> characterMetadata,
            bool hasSource, OrchestrationExecutionSource<TValue> source)
        {
            m_Frames = frames;
            m_FrameSlot = frameSlot;
            m_CharacterValues = characterValues;
            m_CharacterMetadata = characterMetadata;
            m_HasSource = hasSource;
            m_Source = source;
            m_CandidateRootFrameSlot = frameSlot;
        }

        internal void SelectFrame(int frameSlot) => m_FrameSlot = frameSlot;

        public ref readonly TValue ReadValue(int valueSlot, int ownerOperationIndex, int characterSlot)
        {
            if (characterSlot >= 0)
                return ref CharacterValues[characterSlot];
            return ref Values(Owner(ownerOperationIndex))[valueSlot];
        }

        public ref readonly OrchestrationBlackboardMetadata ReadMetadata(int valueSlot,
            int ownerOperationIndex, int characterSlot)
        {
            if (characterSlot >= 0)
                return ref CharacterMetadata[characterSlot];
            return ref Metadata(Owner(ownerOperationIndex))[valueSlot];
        }

        public ref readonly OrchestrationOperationState ReadOperation(int operationIndex, int ownerOperationIndex)
        {
            FrameAddress address = Owner(ownerOperationIndex);
            ReadOnlySpan<OrchestrationOperationState> operations = address.Source
                ? m_Source.Frames.Operations(address.Index) : m_Frames.Operations(address.Index);
            return ref operations[operationIndex];
        }

        public ReadOnlySpan<OrchestrationExecutionFrameState> ReadExecutionFrame(int ownerOperationIndex)
        {
            FrameAddress address = Owner(ownerOperationIndex);
            return address.Source ? m_Source.Frames.HeaderSpan(address.Index) : m_Frames.HeaderSpan(address.Index);
        }

        public ref readonly TValue ReadOutputValue(int valueSlot, int ownerOperationIndex) =>
            ref Values(Owner(ownerOperationIndex))[valueSlot];

        public int OwnerFrameIndex(int ownerOperationIndex) => Owner(ownerOperationIndex).Index;
        public bool IsBorrowedOwner(int ownerOperationIndex) => Owner(ownerOperationIndex).Source;

        public ulong OwnerGeneration(int ownerOperationIndex)
        {
            FrameAddress address = Owner(ownerOperationIndex);
            return address.Source ? m_Source.Frames.Header(address.Index).Generation : m_Frames.Header(address.Index).Generation;
        }

        public ulong FrameTick(int ownerOperationIndex, ulong candidateTick) =>
            Owner(ownerOperationIndex).Source ? m_Source.Tick : candidateTick;

        internal int SourceFrameIndex => m_Source.FrameSlot;
        internal OrchestrationStopContext SourceStopContext => m_Source.StopContext;
        internal ref readonly OrchestrationExecutionFrameState SourceHeader(int frameSlot) =>
            ref m_Source.Frames.Header(frameSlot);

        ReadOnlySpan<TValue> CharacterValues => m_HasSource ? m_Source.CharacterValues : m_CharacterValues;
        ReadOnlySpan<OrchestrationBlackboardMetadata> CharacterMetadata =>
            m_HasSource ? m_Source.CharacterBlackboards : m_CharacterMetadata;

        ReadOnlySpan<TValue> Values(FrameAddress address) => address.Source
            ? m_Source.Frames.Values(address.Index) : m_Frames.Values(address.Index);

        ReadOnlySpan<OrchestrationBlackboardMetadata> Metadata(FrameAddress address) => address.Source
            ? m_Source.Frames.BlackboardMetadata(address.Index) : m_Frames.BlackboardMetadata(address.Index);

        FrameAddress Owner(int ownerOperationIndex)
        {
            if (!m_HasSource)
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
