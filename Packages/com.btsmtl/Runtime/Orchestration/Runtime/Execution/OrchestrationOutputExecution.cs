using System;
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

        void EmitOutput(int operationIndex, ReadOnlySpan<TValue> values)
        {
            var range = new OrchestrationRange(m_Workspace.OutputValueCount, values.Length);
            values.CopyTo(m_Workspace.OutputValues.AsSpan(range.Offset, range.Count));
            m_Workspace.OutputValueCount += values.Length;
            m_Workspace.OutputRequests[m_Workspace.OutputRequestCount++] = new OrchestrationOutputRequest(
                operationIndex, ReadGeneration(operationIndex), m_Input.InstanceGeneration,
                m_Program.Operation(operationIndex).Integer0, range, m_HasOutputTimelineInvocation,
                m_OutputTimelineToken, m_OutputTimelineHook);
        }
    }
}
