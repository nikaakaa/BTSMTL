using System;
using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayActionExtensionContracts
    {
        public static readonly OrchestrationExtensionDefinition ActivationEntry = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.activation-entry", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition ActionContextActive = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.action-context-active", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition ActionEventReceived = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.action-event-received", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition AbilityActive = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.ability-active", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition SelectedSourcePhase = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.selected-source-phase", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition CanActivateAction = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.can-activate-action", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_TargetId", nameof(OrchestrationValueKind.Identity), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_TargetPosition", nameof(OrchestrationValueKind.Vector3), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_TargetYaw", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition SetActionPhase = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.set-action-phase", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                Array.Empty<OrchestrationExtensionPort>()));
    }
}
