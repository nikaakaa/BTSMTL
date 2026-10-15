using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayActionTargetExtensionContracts
    {
        public static readonly OrchestrationExtensionDefinition Read = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.action-target", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_TargetId", nameof(OrchestrationValueKind.Identity), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_Position", nameof(OrchestrationValueKind.Vector3), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_Yaw", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_HasTarget", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_Position", nameof(OrchestrationValueKind.Vector3), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_Yaw", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_PlanarDistance", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition Capture = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.capture-action-target", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    new OrchestrationExtensionPort("m_TargetId", nameof(OrchestrationValueKind.Identity), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_Position", nameof(OrchestrationValueKind.Vector3), OrchestrationExtensionPortDirection.Output),
                    new OrchestrationExtensionPort("m_Yaw", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Output)
                }));
    }
}
