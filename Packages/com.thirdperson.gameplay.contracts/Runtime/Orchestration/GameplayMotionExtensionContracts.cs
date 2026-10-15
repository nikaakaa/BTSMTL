using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayMotionExtensionContracts
    {
        public static readonly OrchestrationExtensionDefinition MoveFacingAngle = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.character-move-facing-angle", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_MoveInput", nameof(OrchestrationValueKind.Vector2), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition CharacterStateVector3 = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.character-state-vector3", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Vector3), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition CharacterStateScalar = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.character-state-scalar", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition CharacterStateYaw = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.character-state-yaw", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Yaw), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition CharacterStateBoolean = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.character-state-bool", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition LocomotionDecision = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.locomotion-decision", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    new OrchestrationExtensionPort("m_Output", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition LocomotionInputMotion = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.locomotion-input-motion", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    new OrchestrationExtensionPort("m_MoveInput", nameof(OrchestrationValueKind.Vector2), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_ElapsedTicks", nameof(OrchestrationValueKind.Int32), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_ContinuationStateOperation", nameof(OrchestrationValueKind.Int32), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_ElapsedTicks", nameof(OrchestrationValueKind.Int32), OrchestrationExtensionPortDirection.Output)
                }));

        public static readonly OrchestrationExtensionDefinition MotionWarpAction = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity("btsmtl-skill", "btsmtl.skill.motion-warp", GameplayOrchestrationBindingRoles.Main),
            1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    new OrchestrationExtensionPort("m_Enabled", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_HasTarget", nameof(OrchestrationValueKind.Boolean), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_TargetPosition", nameof(OrchestrationValueKind.Vector3), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_TargetYaw", nameof(OrchestrationValueKind.Scalar), OrchestrationExtensionPortDirection.Input),
                    new OrchestrationExtensionPort("m_InputDirection", nameof(OrchestrationValueKind.Vector2), OrchestrationExtensionPortDirection.Input)
                }));
    }
}
