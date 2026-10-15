using System;

namespace BTSMTL.Orchestration
{
    public sealed class ReadOnlyOrchestrationExtensionRoute<TValue, TDomainContext> :
        IReadOnlyOrchestrationExtensionRoute<TValue>
        where TValue : struct
        where TDomainContext : struct
    {
        readonly IReadOnlyOrchestrationExtension<TValue, TDomainContext> m_Implementation;

        public ReadOnlyOrchestrationExtensionRoute(
            IReadOnlyOrchestrationExtension<TValue, TDomainContext> implementation, int contextSlotIndex)
        {
            m_Implementation = implementation;
            ContextSlotIndex = contextSlotIndex;
        }

        public OrchestrationExtensionIdentity Identity => m_Implementation.Identity;
        public uint SemanticVersion => m_Implementation.SemanticVersion;
        public int ContextSlotIndex { get; }
        public Type ContextType => typeof(TDomainContext);
        public OrchestrationExtensionContract Describe(int contentIndex) => m_Implementation.Describe(contentIndex);

        public OrchestrationExtensionResult Invoke(int contentIndex, in OrchestrationReadContextView context,
            in OrchestrationExtensionInvocation<TValue> invocation)
        {
            ref readonly TDomainContext domainContext = ref context.Get<TDomainContext>(ContextSlotIndex);
            return m_Implementation.Invoke(contentIndex, in domainContext, in invocation);
        }
    }

    public sealed class CandidateOrchestrationExtensionRoute<TValue, TDomainContext> :
        ICandidateOrchestrationExtensionRoute<TValue>
        where TValue : struct
        where TDomainContext : struct
    {
        readonly ICandidateOrchestrationExtension<TValue, TDomainContext> m_Implementation;

        public CandidateOrchestrationExtensionRoute(
            ICandidateOrchestrationExtension<TValue, TDomainContext> implementation, int contextSlotIndex)
        {
            m_Implementation = implementation;
            ContextSlotIndex = contextSlotIndex;
        }

        public OrchestrationExtensionIdentity Identity => m_Implementation.Identity;
        public uint SemanticVersion => m_Implementation.SemanticVersion;
        public int ContextSlotIndex { get; }
        public Type ContextType => typeof(TDomainContext);
        public OrchestrationExtensionContract Describe(int contentIndex) => m_Implementation.Describe(contentIndex);

        public OrchestrationExtensionResult Invoke(int contentIndex, in OrchestrationCandidateContextView context,
            in OrchestrationExtensionInvocation<TValue> invocation)
        {
            ref TDomainContext domainContext = ref context.Get<TDomainContext>(ContextSlotIndex);
            return m_Implementation.Invoke(contentIndex, ref domainContext, in invocation);
        }
    }
}
