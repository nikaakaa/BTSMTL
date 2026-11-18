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
        readonly OrchestrationProgramStorageView m_CandidateStorage;
        readonly OrchestrationProgramStorageView m_SourceStorage;
        readonly ReadOnlySpan<OrchestrationFrameValueAddress> m_CandidatePrivateAddresses;
        readonly ReadOnlySpan<OrchestrationFrameValueAddress> m_SourcePrivateAddresses;

        internal OrchestrationExecutionSourceReader(ReadOnlyOrchestrationExecutionFrames<TValue> frames,
            int frameSlot, int programIndex, bool hasSource, OrchestrationExecutionSource<TValue> source)
        {
            m_Frames = frames;
            m_FrameSlot = frameSlot;
            m_HasSource = hasSource;
            m_Source = source;
            m_CandidateRootFrameSlot = frameSlot;
            m_CandidateProgramIndex = programIndex;
            m_CandidateStorage = frames.Plan.ProgramStorage(programIndex);
            m_CandidatePrivateAddresses = frames.Plan.PrivateAddresses(programIndex);
            if (hasSource)
            {
                int sourceProgramIndex = source.Frames.Plan.ProgramIndex(source.Header.SkillContentId);
                m_SourceStorage = source.Frames.Plan.ProgramStorage(sourceProgramIndex);
                m_SourcePrivateAddresses = source.Frames.Plan.PrivateAddresses(sourceProgramIndex);
            }
            else
            {
                m_SourceStorage = default;
                m_SourcePrivateAddresses = default;
            }
        }

        internal void SelectFrame(int frameSlot) => m_FrameSlot = frameSlot;

        internal OrchestrationValueAddress CandidateAddress(int frameSlot, int valueSlot) =>
            m_Frames.Plan.ResolveAddress(m_CandidatePrivateAddresses[valueSlot], frameSlot);

        internal int CandidateValueIndex(int valueSlot) => m_CandidateStorage.Values[valueSlot].ValueIndex;

        public void ReadValue<TStorage>(int valueSlot, int ownerOperationIndex,
            int characterSlot, bool actionInstance, ref TValue destination, ref TStorage storage)
            where TStorage : struct, IOrchestrationValueStorage<TValue>
        {
            if (characterSlot >= 0)
            {
                ReadOnlyOrchestrationExecutionFrames<TValue> frames = m_HasSource ? m_Source.SharedFrames : m_Frames;
                OrchestrationValueAddress address = frames.Plan.CharacterAddress(characterSlot);
                storage.Read(frames.Memory, in address, ref destination);
            }
            else if (actionInstance)
            {
                ReadOnlyOrchestrationExecutionFrames<TValue> frames = SharedFrames;
                OrchestrationValueAddress address = frames.Plan.PrivateAddress(
                    m_CandidateProgramIndex, ActionFrameIndex, valueSlot);
                storage.Read(frames.Memory, in address, ref destination);
            }
            else
            {
                FrameAddress owner = Owner(ownerOperationIndex);
                ReadValue(valueSlot, owner, ref destination, ref storage);
            }
        }

        internal bool TryReadValue<TStorage>(int valueSlot, int ownerOperationIndex,
            int characterSlot, bool actionInstance, in OrchestrationBlackboardOwnerToken expected,
            ref TValue destination, ref TStorage storage)
            where TStorage : struct, IOrchestrationValueStorage<TValue>
        {
            if (characterSlot >= 0 || actionInstance)
            {
                if (!ReadMetadata(valueSlot, ownerOperationIndex, characterSlot, actionInstance).Owner.Equals(in expected))
                    return false;
                ReadValue(valueSlot, ownerOperationIndex, characterSlot, actionInstance, ref destination, ref storage);
                return true;
            }
            FrameAddress owner = Owner(ownerOperationIndex);
            if (!ReadMetadata(valueSlot, owner).Owner.Equals(in expected))
                return false;
            ReadValue(valueSlot, owner, ref destination, ref storage);
            return true;
        }

        void ReadValue<TStorage>(int valueSlot, FrameAddress owner, ref TValue destination, ref TStorage storage)
            where TStorage : struct, IOrchestrationValueStorage<TValue>
        {
            ReadOnlyOrchestrationExecutionFrames<TValue> frames = Frames(owner);
            OrchestrationValueAddress address = owner.Source
                ? m_Source.Frames.Plan.ResolveAddress(m_SourcePrivateAddresses[valueSlot], owner.Index)
                : CandidateAddress(owner.Index, valueSlot);
            storage.Read(frames.Memory, in address, ref destination);
        }

        public ref readonly OrchestrationBlackboardMetadata ReadMetadata(int valueSlot,
            int ownerOperationIndex, int characterSlot, bool actionInstance)
        {
            if (characterSlot >= 0)
                return ref SharedFrames.CharacterMetadata[characterSlot];
            if (actionInstance)
                return ref SharedFrames.Element<OrchestrationBlackboardMetadata>(ActionFrameIndex,
                    OrchestrationStateSegmentRole.PrivateMetadata, m_CandidateStorage.Values[valueSlot].ValueIndex);
            FrameAddress owner = Owner(ownerOperationIndex);
            return ref ReadMetadata(valueSlot, owner);
        }

        ref readonly OrchestrationBlackboardMetadata ReadMetadata(int valueSlot, FrameAddress owner)
        {
            ReadOnlyOrchestrationExecutionFrames<TValue> frames = Frames(owner);
            return ref frames.Element<OrchestrationBlackboardMetadata>(owner.Index,
                OrchestrationStateSegmentRole.PrivateMetadata, Storage(owner).Values[valueSlot].ValueIndex);
        }

        ReadOnlyOrchestrationExecutionFrames<TValue> SharedFrames => m_HasSource ? m_Source.SharedFrames : m_Frames;
        int ActionFrameIndex => SharedFrames.ActionFrame(m_HasSource ? m_Source.SharedFrameSlot : m_FrameSlot);
        internal ReadOnlySpan<OrchestrationExecutionFrameState> ReadActionExecutionFrame() =>
            SharedFrames.HeaderSpan(ActionFrameIndex);

        public ref readonly OrchestrationOperationState ReadOperation(int operationIndex, int ownerOperationIndex)
        {
            FrameAddress address = Owner(ownerOperationIndex);
            return ref Frames(address).Element<OrchestrationOperationState>(address.Index,
                OrchestrationStateSegmentRole.Operations, Storage(address).OperationIndices[operationIndex]);
        }

        internal ref readonly OrchestrationOperationState ReadSourceOperation(int operationIndex, int ownerOperationIndex) =>
            ref m_Source.Frames.Element<OrchestrationOperationState>(m_Source.Frames.OwnerFrame(m_Source.FrameSlot, ownerOperationIndex),
                OrchestrationStateSegmentRole.Operations, m_SourceStorage.OperationIndices[operationIndex]);

        public ReadOnlySpan<OrchestrationExecutionFrameState> ReadExecutionFrame(int ownerOperationIndex)
        {
            FrameAddress address = Owner(ownerOperationIndex);
            return address.Source ? m_Source.Frames.HeaderSpan(address.Index) : m_Frames.HeaderSpan(address.Index);
        }

        public ref readonly TValue ReadOutputValue(int valueSlot, int ownerOperationIndex)
        {
            FrameAddress owner = Owner(ownerOperationIndex);
            ReadOnlyOrchestrationExecutionFrames<TValue> frames = Frames(owner);
            return ref frames.Element<TValue>(owner.Index, OrchestrationStateSegmentRole.SavedOutputs,
                Storage(owner).Values[valueSlot].SavedOutputIndex);
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
        internal ReadOnlySpan<OrchestrationStopContext> SourceStopContextStorage => m_Source.StopContextStorage;
        internal ref readonly OrchestrationExecutionFrameState SourceHeader(int frameSlot) =>
            ref m_Source.Frames.Header(frameSlot);

        ReadOnlyOrchestrationExecutionFrames<TValue> Frames(FrameAddress address) =>
            address.Source ? m_Source.Frames : m_Frames;

        OrchestrationProgramStorageView Storage(FrameAddress address) =>
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
