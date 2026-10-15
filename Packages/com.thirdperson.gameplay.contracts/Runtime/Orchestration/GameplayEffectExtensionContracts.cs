using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayEffectExtensionContracts
    {
        public static readonly OrchestrationExtensionDefinition HasTag = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.gameplay-tag-has", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Result", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition MatchTags = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.gameplay-tag-query", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Result", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition SourceAllowsCandidate = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.source-effects-allow-candidate", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Result", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition Active = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.gameplay-effect-active", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Result", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_Handle", nameof(OrchestrationValueKind.UInt64), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_EventTag", nameof(OrchestrationValueKind.Identity), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_EventId", nameof(OrchestrationValueKind.UInt64), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_SourceTick", nameof(OrchestrationValueKind.UInt64), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_AcceptedTick", nameof(OrchestrationValueKind.UInt64), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_SourceActionId", nameof(OrchestrationValueKind.UInt64), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_TargetId", nameof(OrchestrationValueKind.Identity), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_Position", nameof(OrchestrationValueKind.Vector3), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_Yaw", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition AttributeRead = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.gameplay-attribute-read", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Valid", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_BaseValue", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_CurrentValue", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition Apply = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.gameplay-effect-apply", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    new OrchestrationExtensionPort("m_Applied", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_Handle", nameof(OrchestrationValueKind.UInt64), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition Remove = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.gameplay-effect-remove", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    new OrchestrationExtensionPort("m_Handle", nameof(OrchestrationValueKind.UInt64), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_Removed", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));
    }
}
