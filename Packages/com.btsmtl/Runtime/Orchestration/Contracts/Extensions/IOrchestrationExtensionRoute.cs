using System;

namespace BTSMTL.Orchestration
{
    public interface IReadOnlyOrchestrationExtensionRoute<TValue> : IOrchestrationExtension
        where TValue : struct
    {
        int ContextSlotIndex { get; }
        Type ContextType { get; }

        OrchestrationExtensionResult Invoke(int contentIndex, in OrchestrationReadContextView context,
            in OrchestrationExtensionInvocation<TValue> invocation);
    }

    public interface ICandidateOrchestrationExtensionRoute<TValue> : IOrchestrationExtension
        where TValue : struct
    {
        int ContextSlotIndex { get; }
        Type ContextType { get; }

        OrchestrationExtensionResult Invoke(int contentIndex, in OrchestrationCandidateContextView context,
            in OrchestrationExtensionInvocation<TValue> invocation);
    }
}
