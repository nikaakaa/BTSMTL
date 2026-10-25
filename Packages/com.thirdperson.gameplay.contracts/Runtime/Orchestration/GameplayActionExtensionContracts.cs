using System;
using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayActionExtensionContracts
    {
        public const string DomainId = "gameplay.actions";

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
            public static readonly OrchestrationExtensionContentField CandidateAbility =
                OrchestrationExtensionContentField.Identity("CandidateAbility", "gameplay.ability");
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

        public readonly struct BlackboardFacts
        {
            public static readonly OrchestrationExtensionInputPort<BlackboardFacts> CurrentOwnerGeneration =
                new OrchestrationExtensionInputPort<BlackboardFacts>("current-owner-generation", OrchestrationValueKind.UInt64, 0);
            public static readonly OrchestrationExtensionOutputPort<BlackboardFacts> InstanceId =
                new OrchestrationExtensionOutputPort<BlackboardFacts>("instance-id", OrchestrationValueKind.UInt64, 0);
            public static readonly OrchestrationExtensionOutputPort<BlackboardFacts> SkillContent =
                new OrchestrationExtensionOutputPort<BlackboardFacts>("skill-content", OrchestrationValueKind.Identity, 1);
            public static readonly OrchestrationExtensionOutputPort<BlackboardFacts> ExecutionGeneration =
                new OrchestrationExtensionOutputPort<BlackboardFacts>("execution-generation", OrchestrationValueKind.UInt64, 2);
            public static readonly OrchestrationExtensionOutputPort<BlackboardFacts> SegmentGeneration =
                new OrchestrationExtensionOutputPort<BlackboardFacts>("segment-generation", OrchestrationValueKind.UInt64, 3);

            public static readonly OrchestrationExtensionDefinition<BlackboardFacts> Definition = new OrchestrationExtensionDefinition<BlackboardFacts>(
                new OrchestrationExtensionIdentity(DomainId, "blackboard-facts"),
                1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        CurrentOwnerGeneration.Declaration,
                        InstanceId.Declaration,
                        SkillContent.Declaration,
                        ExecutionGeneration.Declaration,
                        SegmentGeneration.Declaration
                    }, Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct ActivationEntry
        {
            public static readonly OrchestrationExtensionOutputPort<ActivationEntry> Result =
                new OrchestrationExtensionOutputPort<ActivationEntry>("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<ActivationEntry> Definition = new OrchestrationExtensionDefinition<ActivationEntry>(
                new OrchestrationExtensionIdentity(DomainId, "activation-entry"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Result.Declaration
                    }, new[] { Fields.ActivationEntryId }));
        }

        public readonly struct ActionContextActive
        {
            public static readonly OrchestrationExtensionOutputPort<ActionContextActive> Result =
                new OrchestrationExtensionOutputPort<ActionContextActive>("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<ActionContextActive> Definition = new OrchestrationExtensionDefinition<ActionContextActive>(
                new OrchestrationExtensionIdentity(DomainId, "action-context-active"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Result.Declaration
                    }, new[] { Fields.ActionContext }));
        }

        public readonly struct ActionEventReceived
        {
            public static readonly OrchestrationExtensionOutputPort<ActionEventReceived> Result =
                new OrchestrationExtensionOutputPort<ActionEventReceived>("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<ActionEventReceived> Definition = new OrchestrationExtensionDefinition<ActionEventReceived>(
                new OrchestrationExtensionIdentity(DomainId, "action-event-received"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Result.Declaration
                    }, new[] { Fields.EventId }));
        }

        public readonly struct AbilityActive
        {
            public static readonly OrchestrationExtensionOutputPort<AbilityActive> Result =
                new OrchestrationExtensionOutputPort<AbilityActive>("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<AbilityActive> Definition = new OrchestrationExtensionDefinition<AbilityActive>(
                new OrchestrationExtensionIdentity(DomainId, "ability-active"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Result.Declaration
                    }, new[]
                    {
                        GameplayOrchestrationPayloadContract.QueryAll, GameplayOrchestrationPayloadContract.QueryAny,
                        GameplayOrchestrationPayloadContract.QueryNone
                    }));
        }

        public readonly struct SelectedSourcePhase
        {
            public static readonly OrchestrationExtensionOutputPort<SelectedSourcePhase> Result =
                new OrchestrationExtensionOutputPort<SelectedSourcePhase>("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<SelectedSourcePhase> Definition = new OrchestrationExtensionDefinition<SelectedSourcePhase>(
                new OrchestrationExtensionIdentity(DomainId, "selected-source-phase"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        Result.Declaration
                    }, new[] { Fields.Phase }));
        }

        public readonly struct RequestTargetDiffersFromSource
        {
            public static readonly OrchestrationExtensionOutputPort<RequestTargetDiffersFromSource> Result =
                new("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<RequestTargetDiffersFromSource> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "request-target-differs-from-source"), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate, new[] { Result.Declaration },
                    Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct CanActivateAction
        {
            public static readonly OrchestrationExtensionInputPort<CanActivateAction> TargetId =
                new OrchestrationExtensionInputPort<CanActivateAction>("target-id", OrchestrationValueKind.Identity, 0);
            public static readonly OrchestrationExtensionInputPort<CanActivateAction> TargetPosition =
                new OrchestrationExtensionInputPort<CanActivateAction>("target-position", OrchestrationValueKind.Vector3, 1);
            public static readonly OrchestrationExtensionInputPort<CanActivateAction> TargetYaw =
                new OrchestrationExtensionInputPort<CanActivateAction>("target-yaw", OrchestrationValueKind.Scalar, 2);
            public static readonly OrchestrationExtensionOutputPort<CanActivateAction> Result =
                new OrchestrationExtensionOutputPort<CanActivateAction>("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<CanActivateAction> Definition = new OrchestrationExtensionDefinition<CanActivateAction>(
                new OrchestrationExtensionIdentity(DomainId, "can-activate-action"),
                5, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[]
                    {
                        TargetId.Declaration,
                        TargetPosition.Declaration,
                        TargetYaw.Declaration,
                        Result.Declaration
                    }, new[]
                    {
                        Fields.CandidateAbility,
                        Fields.TargetSnapshotDeclaration, Fields.TargetSnapshotOwner,
                        Fields.DefaultTargetId, Fields.DefaultTargetPosition, Fields.DefaultTargetYaw
                    }));
        }

        public readonly struct StateTransition
        {
            public static readonly OrchestrationExtensionDefinition<StateTransition> Definition = new OrchestrationExtensionDefinition<StateTransition>(
                new OrchestrationExtensionIdentity(DomainId, "state-transition"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate,
                    Array.Empty<OrchestrationExtensionPort>(), Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct SetActionPhase
        {
            public static readonly OrchestrationExtensionDefinition<SetActionPhase> Definition = new OrchestrationExtensionDefinition<SetActionPhase>(
                new OrchestrationExtensionIdentity(DomainId, "set-action-phase"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate,
                    Array.Empty<OrchestrationExtensionPort>(), new[] { Fields.Phase }));
        }
    }
}
