using System;
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

        void EmitOutput(int operationIndex, ReadOnlySpan<TValue> values) => AppendOutput(operationIndex,
            m_Program.Payload<OrchestrationOutputPayload>(operationIndex).TemplateIndex, values, m_TimelineInvocation.HasInvocation,
            m_TimelineInvocation.Invocation.Token, m_TimelineInvocation.Invocation.Hook);

        void AppendOutput(int operationIndex, int templateIndex, ReadOnlySpan<TValue> values,
            bool hasTimelineInvocation, in OrchestrationTimelineInvocationToken invocation, OrchestrationTimelineHook hook)
        {
            ref OrchestrationExecutionScratchHeader header = ref m_Scratch.Header;
            Span<OrchestrationOutputRequest> requests = m_Scratch.Section<OrchestrationOutputRequest>(header.Layout.OutputRequests);
            Span<TValue> emitted = m_Scratch.Section<TValue>(header.Layout.EmittedValues);
            if (header.OutputRequestCount == requests.Length ||
                emitted.Length - header.OutputValueCount < values.Length)
                Fail(OrchestrationExecutionErrorCode.OutputBudget, operationIndex,
                    required: values.Length, capacity: emitted.Length - header.OutputValueCount);
            var range = new OrchestrationRange(header.OutputValueCount, values.Length);
            values.CopyTo(emitted.Slice(range.Offset, range.Count));
            header.OutputValueCount += values.Length;
            requests[header.OutputRequestCount++] = new OrchestrationOutputRequest(
                operationIndex, ReadGeneration(operationIndex), m_Input.InstanceGeneration,
                templateIndex, range, hasTimelineInvocation, invocation, hook,
                m_TimelineInvocation.Start.SourceIdentityId, m_TimelineInvocation.Start.SourceGeneration,
                m_TimelineInvocation.Invocation.Time);
        }
    }
}
