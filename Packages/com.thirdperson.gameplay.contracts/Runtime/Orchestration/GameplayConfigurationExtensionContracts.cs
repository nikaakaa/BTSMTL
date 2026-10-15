using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayConfigurationExtensionContracts
    {
        public static readonly OrchestrationExtensionDefinition Boolean = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.configuration-bool", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition Int32 = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.configuration-int32", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Int32), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition UInt64 = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.configuration-uint64", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.UInt64), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition Number = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.configuration-number", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition Text = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.configuration-text", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Identity), OrchestrationExtensionPortDirection.Output)
                }));
    }
}
