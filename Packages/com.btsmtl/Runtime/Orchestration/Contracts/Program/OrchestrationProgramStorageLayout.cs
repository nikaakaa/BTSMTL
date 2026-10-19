using System;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationValueStorageSlot
    {
        public OrchestrationValueStorageSlot(int ownerOperationIndex, int valueIndex, int typeIndex,
            int savedOutputIndex, int metadataIndex)
        {
            OwnerOperationIndex = ownerOperationIndex;
            ValueIndex = valueIndex;
            TypeIndex = typeIndex;
            SavedOutputIndex = savedOutputIndex;
            MetadataIndex = metadataIndex;
        }

        public int OwnerOperationIndex { get; }
        public int ValueIndex { get; }
        public int TypeIndex { get; }
        public int SavedOutputIndex { get; }
        public int MetadataIndex { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public readonly partial struct OrchestrationExecutionOwnerStorage
    {
        public OrchestrationExecutionOwnerStorage(int operationCount, int savedOutputCount,
            int blackboardCount, ReadOnlyMemory<int> valueKindCounts)
        {
            OperationCount = operationCount;
            SavedOutputCount = savedOutputCount;
            BlackboardCount = blackboardCount;
            ValueKindCounts = valueKindCounts;
        }

        public int OperationCount { get; }
        public int SavedOutputCount { get; }
        public int BlackboardCount { get; }
        public ReadOnlyMemory<int> ValueKindCounts { get; }
    }

    [ValueStorage(UseConstructor = true)]
    public sealed partial class OrchestrationProgramStorageLayout
    {
        public const ushort NoOperationIndex = ushort.MaxValue;

        public OrchestrationProgramStorageLayout(ReadOnlyMemory<ushort> operationIndices,
            ReadOnlyMemory<OrchestrationValueStorageSlot> values,
            ReadOnlyMemory<OrchestrationExecutionOwnerStorage> owners)
        {
            OperationIndices = operationIndices;
            Values = values;
            Owners = owners;
        }

        public ReadOnlyMemory<ushort> OperationIndices { get; }
        public ReadOnlyMemory<OrchestrationValueStorageSlot> Values { get; }
        public ReadOnlyMemory<OrchestrationExecutionOwnerStorage> Owners { get; }
    }
}
