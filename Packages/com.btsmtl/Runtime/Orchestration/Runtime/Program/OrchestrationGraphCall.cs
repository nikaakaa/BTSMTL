using System;

namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationGraphParameter
    {
        public OrchestrationGraphParameter(int portIndex, int valueSlot,
            OrchestrationValueKind valueKind)
        {
            PortIndex = portIndex;
            ValueSlot = valueSlot;
            ValueKind = valueKind;
        }

        public int PortIndex { get; }
        public int ValueSlot { get; }
        public OrchestrationValueKind ValueKind { get; }
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
