using System;

namespace BTSMTL.Orchestration
{
    internal readonly ref struct OrchestrationBlackboardStorageView<TValue>
        where TValue : unmanaged
    {
        readonly Span<TValue> m_Values;
        readonly Span<OrchestrationBlackboardMetadata> m_Metadata;
        readonly Span<TValue> m_CharacterValues;
        readonly Span<OrchestrationBlackboardMetadata> m_CharacterMetadata;
        readonly ReadOnlySpan<int> m_CharacterSlots;

        internal OrchestrationBlackboardStorageView(Span<TValue> values,
            Span<OrchestrationBlackboardMetadata> metadata, Span<TValue> characterValues,
            Span<OrchestrationBlackboardMetadata> characterMetadata, ReadOnlySpan<int> characterSlots)
        {
            m_Values = values;
            m_Metadata = metadata;
            m_CharacterValues = characterValues;
            m_CharacterMetadata = characterMetadata;
            m_CharacterSlots = characterSlots;
        }

        internal ref TValue Value(int valueSlot)
        {
            int characterSlot = m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot];
            if (characterSlot >= 0)
                return ref m_CharacterValues[characterSlot];
            return ref m_Values[valueSlot];
        }

        internal ref OrchestrationBlackboardMetadata Metadata(int valueSlot)
        {
            int characterSlot = m_CharacterSlots.IsEmpty ? -1 : m_CharacterSlots[valueSlot];
            if (characterSlot >= 0)
                return ref m_CharacterMetadata[characterSlot];
            return ref m_Metadata[valueSlot];
        }
    }
}
