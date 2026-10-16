using System;
using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayActionExtensionContracts
    {
        public const string DomainId = "gameplay.actions";

        public static class Ports
        {
            public static readonly OrchestrationExtensionOutputPort Result =
                new OrchestrationExtensionOutputPort("result", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionInputPort TargetId =
                new OrchestrationExtensionInputPort("target-id", OrchestrationValueKind.Identity, 0);
            public static readonly OrchestrationExtensionInputPort TargetPosition =
                new OrchestrationExtensionInputPort("target-position", OrchestrationValueKind.Vector3, 1);
            public static readonly OrchestrationExtensionInputPort TargetYaw =
                new OrchestrationExtensionInputPort("target-yaw", OrchestrationValueKind.Scalar, 2);
        }

        public static class Fields
        {
            public static readonly OrchestrationExtensionContentField ActivationEntryId =
                OrchestrationExtensionContentField.Identity("ActivationEntryId", "gameplay.activation-entry");
            public static readonly OrchestrationExtensionContentField ActionContext =
                OrchestrationExtensionContentField.Identity("ActionContext", "gameplay.action-context");
            public static readonly OrchestrationExtensionContentField EventId =
                OrchestrationExtensionContentField.Identity("EventId", "gameplay.action-event");
            public static readonly OrchestrationExtensionContentField Phase =
                OrchestrationExtensionContentField.Enum<ThirdPersonSimulation.AbilityLifecyclePhase>("Phase");
            public static readonly OrchestrationExtensionContentField ActionId =
                OrchestrationExtensionContentField.Identity("ActionId", "gameplay.action");
            public static readonly OrchestrationExtensionContentField CandidateAbility =
                OrchestrationExtensionContentField.Identity("CandidateAbility", "gameplay.ability");
            public static readonly OrchestrationExtensionContentField AdmissionProfile =
                OrchestrationExtensionContentField.Identity("AdmissionProfile", "gameplay.ability-admission-profile");
            public static readonly OrchestrationExtensionContentField TargetSnapshotDeclaration =
                OrchestrationExtensionContentField.Identity("TargetSnapshotDeclaration", "btsmtl.blackboard-declaration");
            public static readonly OrchestrationExtensionContentField TargetSnapshotOwner =
                OrchestrationExtensionContentField.Identity("TargetSnapshotOwner", "btsmtl.blackboard-owner");
            public static readonly OrchestrationExtensionContentField DefaultTargetId =
                OrchestrationExtensionContentField.Identity("DefaultTargetId", "gameplay.actor", false);
            public static readonly OrchestrationExtensionContentField DefaultTargetPosition =
                OrchestrationExtensionContentField.Value("DefaultTargetPosition", OrchestrationValueKind.Vector3, false);
            public static readonly OrchestrationExtensionContentField DefaultTargetYaw =
                OrchestrationExtensionContentField.Value("DefaultTargetYaw", OrchestrationValueKind.Scalar, false);
        }

        public static readonly OrchestrationExtensionDefinition ActivationEntry = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "activation-entry"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Result.Declaration
                }, new[] { Fields.ActivationEntryId }));

        public static readonly OrchestrationExtensionDefinition ActionContextActive = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "action-context-active"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Result.Declaration
                }, new[] { Fields.ActionContext }));

        public static readonly OrchestrationExtensionDefinition ActionEventReceived = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "action-event-received"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Result.Declaration
                }, new[] { Fields.EventId }));

        public static readonly OrchestrationExtensionDefinition AbilityActive = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "ability-active"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Result.Declaration
                }, new[]
                {
                    GameplayOrchestrationPayloadContract.QueryAll, GameplayOrchestrationPayloadContract.QueryAny,
                    GameplayOrchestrationPayloadContract.QueryNone
                }));

        public static readonly OrchestrationExtensionDefinition SelectedSourcePhase = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "selected-source-phase"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Result.Declaration
                }, new[] { Fields.Phase }));

        public static readonly OrchestrationExtensionDefinition CanActivateAction = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "can-activate-action"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.TargetId.Declaration,
                    Ports.TargetPosition.Declaration,
                    Ports.TargetYaw.Declaration,
                    Ports.Result.Declaration
                }, new[]
                {
                    Fields.ActionId, Fields.CandidateAbility, Fields.AdmissionProfile,
                    Fields.TargetSnapshotDeclaration, Fields.TargetSnapshotOwner,
                    Fields.DefaultTargetId, Fields.DefaultTargetPosition, Fields.DefaultTargetYaw
                }));

        public static readonly OrchestrationExtensionDefinition StateTransition = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "state-transition"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                Array.Empty<OrchestrationExtensionPort>(), Array.Empty<OrchestrationExtensionContentField>()));

        public static readonly OrchestrationExtensionDefinition SetActionPhase = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "set-action-phase"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                Array.Empty<OrchestrationExtensionPort>(), new[] { Fields.Phase }));
    }
}
