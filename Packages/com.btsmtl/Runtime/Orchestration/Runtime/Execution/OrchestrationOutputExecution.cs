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

        void EmitOutput(int operationIndex, ReadOnlySpan<TValue> values) => AppendOutput(operationIndex,
            m_Program.Operation(operationIndex).Integer0, values, m_TimelineInvocation.HasInvocation,
            m_TimelineInvocation.Invocation.Token, m_TimelineInvocation.Invocation.Hook);

        void AppendOutput(int operationIndex, int templateIndex, ReadOnlySpan<TValue> values,
            bool hasTimelineInvocation, in OrchestrationTimelineInvocationToken invocation, OrchestrationTimelineHook hook)
        {
            if (m_Workspace.OutputRequestCount == m_Workspace.OutputRequests.Length ||
                m_Workspace.OutputValues.Length - m_Workspace.OutputValueCount < values.Length)
                throw new InvalidOperationException($"Operation '{operationIndex}' exceeds the published output budget of its program invocation.");
            var range = new OrchestrationRange(m_Workspace.OutputValueCount, values.Length);
            values.CopyTo(m_Workspace.OutputValues.AsSpan(range.Offset, range.Count));
            m_Workspace.OutputValueCount += values.Length;
            m_Workspace.OutputRequests[m_Workspace.OutputRequestCount++] = new OrchestrationOutputRequest(
                operationIndex, ReadGeneration(operationIndex), m_Input.InstanceGeneration,
                templateIndex, range, hasTimelineInvocation, invocation, hook,
                m_TimelineInvocation.Start.SourceIdentityId, m_TimelineInvocation.Start.SourceGeneration,
                m_TimelineInvocation.Invocation.Time);
        }
    }
}
