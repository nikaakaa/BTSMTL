using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public ref partial struct OrchestrationExecutionCursor<TValue, TScalar, TVector2,
        TVector3, TYaw, TQuaternion, TMath, TValues, TContext, TDiagnostics, TStorage, THost>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TMath : struct, IOrchestrationGraphMath<TScalar, TVector2, TVector3, TQuaternion>
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TContext : struct
        where TDiagnostics : struct, IOrchestrationExecutionDiagnostics<TValue>
        where TStorage : struct, IOrchestrationValueStorage<TValue>
        where THost : struct, IOrchestrationHost<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TMath, TValues, TContext>
    {

        OrchestrationOperationResult TickRoot(int operationIndex)
        {
            if (m_Contract != OrchestrationExecutionContract.Complete)
                return TickSingleChild(operationIndex, OrchestrationFlowKind.Child);
            if (m_GraphCallDepth + 1 > m_GraphCalls.Length)
                Fail(OrchestrationExecutionErrorCode.WorkspaceGraphCallDepth, operationIndex,
                    required: m_GraphCallDepth + 1, capacity: m_GraphCalls.Length);
            m_GraphCalls[m_GraphCallDepth++] =
                new OrchestrationGraphCallContext
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
                OperationState(operationIndex).Status = (OrchestrationOperationStatus)result;
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
                if (m_GraphCalls[i].ExecutionFrameIndex == frameSlot &&
                    m_GraphCalls[i].OperationIndex == ownerOperationIndex)
                    return true;
            return false;
        }
    }
}
