using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class CombatExtensionContracts
    {
        public static readonly OrchestrationExtensionDefinition AttackQuery = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.attack-query", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    new OrchestrationExtensionPort("m_Submitted", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition AttackHitQuery = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.attack-hit-query", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Hit", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));
    }
}
