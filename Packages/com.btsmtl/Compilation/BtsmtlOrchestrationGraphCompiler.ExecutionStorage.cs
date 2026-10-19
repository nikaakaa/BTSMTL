using System;
using BTSMTL.Orchestration;

namespace BTSMTL.Compilation
{
    public static partial class BtsmtlOrchestrationGraphCompiler
    {
        sealed partial class Compilation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues, TLowering>
        {
            OrchestrationProgramStorageLayout BuildExecutionStorage(OperationDraft[] operations,
                OrchestrationValueSlotType[] valueSlotTypes)
            {
                int kindCount = Enum.GetValues(typeof(OrchestrationValueKind)).Length;
                var operationIndices = new ushort[operations.Length];
                var operationCounts = new int[operations.Length];
                var savedOutputCounts = new int[operations.Length];
                var blackboardCounts = new int[operations.Length];
                var valueKindCounts = new int[operations.Length][];
                for (int operationIndex = 0; operationIndex < operations.Length; operationIndex++)
                {
                    OperationDraft operation = operations[operationIndex];
                    int owner = operation.ExecutionFrameOwnerOperationIndex;
                    if (valueKindCounts[owner] == null)
                        valueKindCounts[owner] = new int[kindCount];
                    operationIndices[operationIndex] = RequiresOperationStorage(operation)
                        ? CompactIndex(operationCounts[owner]++, operationIndex, "ExecutionStorage.OperationIndex")
                        : OrchestrationProgramStorageLayout.NoOperationIndex;
                }

                var values = new OrchestrationValueStorageSlot[valueSlotTypes.Length];
                for (int valueSlot = 0; valueSlot < values.Length; valueSlot++)
                    values[valueSlot] = new OrchestrationValueStorageSlot(-1, -1, -1, -1, -1);
                for (int stateSlot = 0; stateSlot < m_StateSlots.Count; stateSlot++)
                {
                    if (m_StateSlots[stateSlot].Semantic != ProgramStateSemantic.BlackboardValue)
                        continue;
                    ProgramScopeLayout scope = Scope(stateSlot);
                    if (scope.Kind == ProgramScopeKind.Character)
                        continue;
                    int valueSlot = m_StateValueSlots[stateSlot];
                    int owner = ExecutionFrameOwner(scope);
                    int valueIndex = blackboardCounts[owner]++;
                    int typeIndex = valueKindCounts[owner][(int)valueSlotTypes[valueSlot].ValueKind]++;
                    OrchestrationValueStorageSlot previous = values[valueSlot];
                    values[valueSlot] = new OrchestrationValueStorageSlot(owner, valueIndex, typeIndex,
                        previous.SavedOutputIndex, valueIndex);
                }
                for (int operationIndex = 0; operationIndex < m_OutputOffsets.Length; operationIndex++)
                {
                    int start = m_OutputOffsets[operationIndex];
                    if (start < 0)
                        continue;
                    int owner = operations[operationIndex].ExecutionFrameOwnerOperationIndex;
                    int count = operations[operationIndex].OutputCount;
                    for (int outputIndex = 0; outputIndex < count; outputIndex++)
                    {
                        int valueSlot = start + outputIndex;
                        OrchestrationValueStorageSlot previous = values[valueSlot];
                        values[valueSlot] = new OrchestrationValueStorageSlot(owner, previous.ValueIndex,
                            previous.TypeIndex, savedOutputCounts[owner]++, previous.MetadataIndex);
                    }
                }

                var owners = new OrchestrationExecutionOwnerStorage[operations.Length];
                for (int owner = 0; owner < owners.Length; owner++)
                    if (valueKindCounts[owner] != null)
                        owners[owner] = new OrchestrationExecutionOwnerStorage(operationCounts[owner],
                            savedOutputCounts[owner], blackboardCounts[owner], valueKindCounts[owner]);
                return new OrchestrationProgramStorageLayout(operationIndices, values, owners);
            }

            static bool RequiresOperationStorage(OperationDraft operation) => operation.IsRunnable ||
                operation.Code is OrchestrationOperationCode.SubGraph or OrchestrationOperationCode.Loop or
                    OrchestrationOperationCode.Parallel or OrchestrationOperationCode.Sequence or
                    OrchestrationOperationCode.Selector or OrchestrationOperationCode.Cycle or
                    OrchestrationOperationCode.StateMachine or OrchestrationOperationCode.State or
                    OrchestrationOperationCode.Timeline;
        }
    }
}
