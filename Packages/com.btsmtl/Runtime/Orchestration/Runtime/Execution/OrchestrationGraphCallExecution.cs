using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TQuaternion, TMath, TValues, TContext>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
        where TContext : struct
    {

        OrchestrationOperationResult TickRoot(int operationIndex)
        {
            if (m_Contract != OrchestrationExecutionContract.Complete)
                return TickSingleChild(operationIndex, OrchestrationFlowKind.Child);
            m_Workspace.GraphCallEntries[m_GraphCallDepth++] =
                new OrchestrationExecutionWorkspace<TValue>.GraphCallContext
                {
                    ExecutionFrameIndex = m_FrameSlot,
                    OperationIndex = operationIndex
                };
            try
            {
                return TickChildren(operationIndex);
            }
            finally
            {
                m_GraphCallDepth--;
            }
        }

        OrchestrationOperationResult TickGraphCall(int operationIndex)
        {
            PrepareGraphCall(operationIndex);
            OrchestrationOperationResult result = TickSingleChild(operationIndex, OrchestrationFlowKind.Enter);
            if (!m_Program.Operation(operationIndex).IsRunnable)
                m_Operations[operationIndex].Status = (OrchestrationOperationStatus)result;
            return result;
        }

        bool IsGraphScopeActive(int ownerOperationIndex)
        {
            if (m_Blackboards.IsBorrowedOperation(ownerOperationIndex))
                return m_Blackboards.IsBorrowedGraphScope(ownerOperationIndex);
            int frameSlot = OperationFrame(ownerOperationIndex);
            if (IsActive(ownerOperationIndex))
                return true;
            for (int i = m_GraphCallDepth - 1; i >= 0; i--)
                if (m_Workspace.GraphCallEntries[i].ExecutionFrameIndex == frameSlot &&
                    m_Workspace.GraphCallEntries[i].OperationIndex == ownerOperationIndex)
                    return true;
            return false;
        }
    }
}
