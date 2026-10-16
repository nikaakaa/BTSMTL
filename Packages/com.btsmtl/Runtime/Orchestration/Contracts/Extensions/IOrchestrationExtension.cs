namespace BTSMTL.Orchestration
{
    public interface IOrchestrationExtension
    {
        OrchestrationExtensionIdentity Identity { get; }
        uint SemanticVersion { get; }
        OrchestrationExtensionContract Describe(int contentIndex);
    }

    public abstract class ReadOnlyOrchestrationExtensionBinding<TValue, TReadContext> : IOrchestrationExtension
        where TValue : struct
        where TReadContext : struct
    {
        readonly OrchestrationExtensionDefinition m_Definition;

        private protected ReadOnlyOrchestrationExtensionBinding(OrchestrationExtensionDefinition definition)
        {
            m_Definition = definition;
        }

        public OrchestrationExtensionIdentity Identity => m_Definition.Identity;
        public uint SemanticVersion => m_Definition.SemanticVersion;
        public OrchestrationExtensionContract Describe(int contentIndex) => m_Definition.Contract;

        public abstract OrchestrationExtensionResult Dispatch(int contentIndex, in TReadContext context,
            in OrchestrationExtensionArguments<TValue> arguments);
    }

    public abstract class CandidateOrchestrationExtensionBinding<TValue, TCandidateContext> : IOrchestrationExtension
        where TValue : struct
        where TCandidateContext : struct
    {
        readonly OrchestrationExtensionDefinition m_Definition;

        private protected CandidateOrchestrationExtensionBinding(OrchestrationExtensionDefinition definition)
        {
            m_Definition = definition;
        }

        public OrchestrationExtensionIdentity Identity => m_Definition.Identity;
        public uint SemanticVersion => m_Definition.SemanticVersion;
        public OrchestrationExtensionContract Describe(int contentIndex) => m_Definition.Contract;

        public abstract OrchestrationExtensionResult Dispatch(int contentIndex, ref TCandidateContext context,
            in OrchestrationExtensionArguments<TValue> arguments);
    }

    public abstract class ReadOnlyOrchestrationExtension<TValue, TReadContext, TOperation> :
        ReadOnlyOrchestrationExtensionBinding<TValue, TReadContext>
        where TValue : struct
        where TReadContext : struct
        where TOperation : struct
    {
        protected ReadOnlyOrchestrationExtension(in OrchestrationExtensionDefinition<TOperation> definition)
            : base(definition.Declaration)
        {
        }

        public abstract OrchestrationExtensionResult Invoke(int contentIndex, in TReadContext context,
            in OrchestrationExtensionInvocation<TValue, TOperation> invocation);

        public sealed override OrchestrationExtensionResult Dispatch(
            int contentIndex, in TReadContext context, in OrchestrationExtensionArguments<TValue> arguments)
        {
            var invocation = new OrchestrationExtensionInvocation<TValue, TOperation>(in arguments);
            return Invoke(contentIndex, in context, in invocation);
        }
    }

    public abstract class CandidateOrchestrationExtension<TValue, TCandidateContext, TOperation> :
        CandidateOrchestrationExtensionBinding<TValue, TCandidateContext>
        where TValue : struct
        where TCandidateContext : struct
        where TOperation : struct
    {
        protected CandidateOrchestrationExtension(in OrchestrationExtensionDefinition<TOperation> definition)
            : base(definition.Declaration)
        {
        }

        public abstract OrchestrationExtensionResult Invoke(int contentIndex, ref TCandidateContext context,
            in OrchestrationExtensionInvocation<TValue, TOperation> invocation);

        public sealed override OrchestrationExtensionResult Dispatch(
            int contentIndex, ref TCandidateContext context, in OrchestrationExtensionArguments<TValue> arguments)
        {
            var invocation = new OrchestrationExtensionInvocation<TValue, TOperation>(in arguments);
            return Invoke(contentIndex, ref context, in invocation);
        }
    }
}
