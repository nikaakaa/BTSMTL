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
            public static readonly OrchestrationExtensionContentField DefaultTargetObjectHandle =
                OrchestrationExtensionContentField.Value("DefaultTargetObjectHandle", OrchestrationValueKind.ObjectHandle, false);
        }

        public readonly struct BlackboardFacts
        {
            static readonly OrchestrationExtensionPorts<BlackboardFacts> Ports = new();
            public static readonly OrchestrationExtensionInputPort<BlackboardFacts> CurrentOwnerGeneration =
                Ports.Input("current-owner-generation", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionOutputPort<BlackboardFacts> InstanceId =
                Ports.Output("instance-id", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionOutputPort<BlackboardFacts> SkillContent =
                Ports.Output("skill-content", OrchestrationValueKind.Identity);
            public static readonly OrchestrationExtensionOutputPort<BlackboardFacts> ExecutionGeneration =
                Ports.Output("execution-generation", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionOutputPort<BlackboardFacts> SegmentGeneration =
                Ports.Output("segment-generation", OrchestrationValueKind.UInt64);

            public static readonly OrchestrationExtensionDefinition<BlackboardFacts> Definition = new OrchestrationExtensionDefinition<BlackboardFacts>(
                new OrchestrationExtensionIdentity(DomainId, "blackboard-facts"),
                1, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct ActivationEntry
        {
            static readonly OrchestrationExtensionPorts<ActivationEntry> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<ActivationEntry> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<ActivationEntry> Definition = new OrchestrationExtensionDefinition<ActivationEntry>(
                new OrchestrationExtensionIdentity(DomainId, "activation-entry"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { Fields.ActivationEntryId }));
        }

        public readonly struct ActionContextActive
        {
            static readonly OrchestrationExtensionPorts<ActionContextActive> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<ActionContextActive> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<ActionContextActive> Definition = new OrchestrationExtensionDefinition<ActionContextActive>(
                new OrchestrationExtensionIdentity(DomainId, "action-context-active"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { Fields.ActionContext }));
        }

        public readonly struct ActionEventReceived
        {
            static readonly OrchestrationExtensionPorts<ActionEventReceived> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<ActionEventReceived> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<ActionEventReceived> Definition = new OrchestrationExtensionDefinition<ActionEventReceived>(
                new OrchestrationExtensionIdentity(DomainId, "action-event-received"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { Fields.EventId }));
        }

        public readonly struct AbilityActive
        {
            static readonly OrchestrationExtensionPorts<AbilityActive> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<AbilityActive> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<AbilityActive> Definition = new OrchestrationExtensionDefinition<AbilityActive>(
                new OrchestrationExtensionIdentity(DomainId, "ability-active"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[]
                    {
                        GameplayOrchestrationPayloadContract.QueryAll, GameplayOrchestrationPayloadContract.QueryAny,
                        GameplayOrchestrationPayloadContract.QueryNone
                    }));
        }

        public readonly struct SelectedSourcePhase
        {
            static readonly OrchestrationExtensionPorts<SelectedSourcePhase> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<SelectedSourcePhase> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<SelectedSourcePhase> Definition = new OrchestrationExtensionDefinition<SelectedSourcePhase>(
                new OrchestrationExtensionIdentity(DomainId, "selected-source-phase"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { Fields.Phase }));
        }

        public readonly struct RequestTargetDiffersFromSource
        {
            static readonly OrchestrationExtensionPorts<RequestTargetDiffersFromSource> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<RequestTargetDiffersFromSource> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<RequestTargetDiffersFromSource> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "request-target-differs-from-source"), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate, Ports.Seal(),
                    Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct CanActivateAction
        {
            static readonly OrchestrationExtensionPorts<CanActivateAction> Ports = new();
            public static readonly OrchestrationExtensionInputPort<CanActivateAction> TargetId =
                Ports.Input("target-id", OrchestrationValueKind.Identity);
            public static readonly OrchestrationExtensionInputPort<CanActivateAction> TargetPosition =
                Ports.Input("target-position", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionInputPort<CanActivateAction> TargetYaw =
                Ports.Input("target-yaw", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionInputPort<CanActivateAction> TargetObjectHandle =
                Ports.Input("target-object-handle", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<CanActivateAction> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<CanActivateAction> Definition = new OrchestrationExtensionDefinition<CanActivateAction>(
                new OrchestrationExtensionIdentity(DomainId, "can-activate-action"),
                6, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[]
                    {
                        Fields.CandidateAbility,
                        Fields.TargetSnapshotDeclaration, Fields.TargetSnapshotOwner,
                        Fields.DefaultTargetId, Fields.DefaultTargetPosition, Fields.DefaultTargetYaw, Fields.DefaultTargetObjectHandle
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
