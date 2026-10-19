using System;

namespace BTSMTL.Orchestration
{
    public ref struct OrchestrationExecutionSourceReader<TValue> where TValue : unmanaged
    {
        readonly ReadOnlyOrchestrationExecutionFrames<TValue> m_Frames;
        int m_FrameSlot;
        readonly bool m_HasSource;
        readonly OrchestrationExecutionSource<TValue> m_Source;
        readonly int m_CandidateRootFrameSlot;
        readonly int m_CandidateProgramIndex;
        readonly int m_SourceProgramIndex;
        readonly OrchestrationProgramStorageLayout m_CandidateStorage;
        readonly OrchestrationProgramStorageLayout m_SourceStorage;

        internal OrchestrationExecutionSourceReader(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            int frameSlot, int programIndex, bool hasSource, OrchestrationExecutionSource<TValue> source)
        {
            m_Frames = frames;
            m_FrameSlot = frameSlot;
            m_HasSource = hasSource;
            m_Source = source;
            m_CandidateRootFrameSlot = frameSlot;
            m_CandidateProgramIndex = programIndex;
            m_CandidateStorage = frames.Plan.Programs.Span[programIndex].Storage;
            m_SourceProgramIndex = hasSource
                ? source.Frames.Plan.ProgramIndex(source.Header.SkillContentId) : -1;
            m_SourceStorage = hasSource
                ? source.Frames.Plan.Programs.Span[m_SourceProgramIndex].Storage : null;
        }

        internal void SelectFrame(int frameSlot) => m_FrameSlot = frameSlot;

        public void ReadValue<TStorage>(int valueSlot, int ownerOperationIndex,
            int characterSlot, ref TValue destination)
            where TStorage : struct, IOrchestrationValueStorage<TValue>
        {
            if (characterSlot >= 0)
            {
                ReadOnlyOrchestrationExecutionFrames<TValue> frames = m_HasSource ? m_Source.Frames : m_Frames;
                OrchestrationValueAddress address = frames.Plan.CharacterAddress(characterSlot);
                default(TStorage).Read(frames.Memory, in address, ref destination);
            }
            else
            {
                FrameAddress owner = Owner(ownerOperationIndex);
                ReadOnlyOrchestrationExecutionFrames<TValue> frames = Frames(owner);
                OrchestrationValueAddress address = frames.Plan.PrivateAddress(
                    ProgramIndex(owner), owner.Index, valueSlot);
                default(TStorage).Read(frames.Memory, in address, ref destination);
            }
        }

        public ref readonly OrchestrationBlackboardMetadata ReadMetadata(int valueSlot,
            int ownerOperationIndex, int characterSlot)
        {
            if (characterSlot >= 0)
                return ref (m_HasSource ? m_Source.Frames.CharacterMetadata : m_Frames.CharacterMetadata)[characterSlot];
            FrameAddress owner = Owner(ownerOperationIndex);
            ReadOnlyOrchestrationExecutionFrames<TValue> frames = Frames(owner);
            return ref frames.Payload<OrchestrationBlackboardMetadata>(owner.Index,
                OrchestrationStateSegmentRole.PrivateMetadata, ProgramIndex(owner))[
                Storage(owner).Values.Span[valueSlot].MetadataIndex];
        }

        public ref readonly OrchestrationOperationState ReadOperation(int operationIndex, int ownerOperationIndex)
        {
            FrameAddress address = Owner(ownerOperationIndex);
            return ref Frames(address).Payload<OrchestrationOperationState>(address.Index,
                OrchestrationStateSegmentRole.Operations, ProgramIndex(address))[
                Storage(address).OperationIndices.Span[operationIndex]];
        }

        public ReadOnlySpan<OrchestrationExecutionFrameState> ReadExecutionFrame(int ownerOperationIndex)
        {
            FrameAddress address = Owner(ownerOperationIndex);
            return address.Source ? m_Source.Frames.HeaderSpan(address.Index) : m_Frames.HeaderSpan(address.Index);
        }

        public ref readonly TValue ReadOutputValue(int valueSlot, int ownerOperationIndex)
        {
            FrameAddress owner = Owner(ownerOperationIndex);
            ReadOnlyOrchestrationExecutionFrames<TValue> frames = Frames(owner);
            return ref frames.Payload<TValue>(owner.Index, OrchestrationStateSegmentRole.SavedOutputs,
                ProgramIndex(owner))[Storage(owner).Values.Span[valueSlot].SavedOutputIndex];
        }

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

        ReadOnlyOrchestrationExecutionFrames<TValue> Frames(FrameAddress address) =>
            address.Source ? m_Source.Frames : m_Frames;

        int ProgramIndex(FrameAddress address) =>
            address.Source ? m_SourceProgramIndex : m_CandidateProgramIndex;

        OrchestrationProgramStorageLayout Storage(FrameAddress address) =>
            address.Source ? m_SourceStorage : m_CandidateStorage;

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
