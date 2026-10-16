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

        internal OrchestrationBlackboardStorageView(OrchestrationProgram<TValue> program,
            OrchestrationExecutionFrames<TValue> frames, int frameSlot, Span<TValue> characterValues,
            Span<OrchestrationBlackboardMetadata> characterMetadata, ReadOnlySpan<int> characterSlots)
        {
            m_Program = program;
            m_Frames = frames;
            m_FrameSlot = frameSlot;
            m_CharacterValues = characterValues;
            m_CharacterMetadata = characterMetadata;
            m_CharacterSlots = characterSlots;
        }

        internal void SelectFrame(int frameSlot) => m_FrameSlot = frameSlot;

        internal int OwnerFrame(int valueSlot) => m_Frames.OwnerFrame(m_FrameSlot,
            m_Program.Blackboard(valueSlot).ExecutionFrameOwnerOperationIndex);

        internal ref TValue Value(int valueSlot)
        {
            int characterSlot = m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot];
            if (characterSlot >= 0)
                return ref m_CharacterValues[characterSlot];
            return ref m_Frames.Values(OwnerFrame(valueSlot))[valueSlot];
        }

        internal ref OrchestrationBlackboardMetadata Metadata(int valueSlot)
        {
            int characterSlot = m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot];
            if (characterSlot >= 0)
                return ref m_CharacterMetadata[characterSlot];
            return ref m_Frames.BlackboardMetadata(OwnerFrame(valueSlot))[valueSlot];
        }
    }
}
