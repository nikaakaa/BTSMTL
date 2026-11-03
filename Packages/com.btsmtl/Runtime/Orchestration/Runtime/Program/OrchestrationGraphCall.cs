using System;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationGraphParameter
    {
        public OrchestrationGraphParameter(int valueSlot, OrchestrationValueKind valueKind)
        {
            ValueSlot = valueSlot;
            m_ValueKind = (int)valueKind;
        }

        public int ValueSlot { get; }
        readonly int m_ValueKind;
        public OrchestrationValueKind ValueKind => (OrchestrationValueKind)m_ValueKind;
    }

    public sealed class OrchestrationGraphCall
    {
        public OrchestrationGraphCall(int ownerOperationIndex, int entryOperationIndex,
            ReadOnlyMemory<OrchestrationGraphParameter> inputs,
            ReadOnlyMemory<OrchestrationGraphParameter> outputs)
        {
            OwnerOperationIndex = ownerOperationIndex;
            EntryOperationIndex = entryOperationIndex;
            Inputs = inputs;
            Outputs = outputs;
        }

        public int OwnerOperationIndex { get; }
        public int EntryOperationIndex { get; }
        public ReadOnlyMemory<OrchestrationGraphParameter> Inputs { get; }
        public ReadOnlyMemory<OrchestrationGraphParameter> Outputs { get; }
    }
}
