using System;
using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public enum GameplayAttributeReadTarget : byte
    {
        Self = 0,
        ObjectHandle = 1
    }

    public static class GameplayEffectExtensionContracts
    {
        public const string DomainId = "gameplay.effects";

        public static class Fields
        {
            public static readonly OrchestrationExtensionContentField QueryTarget =
                OrchestrationExtensionContentField.Enum<ThirdPersonSimulation.GameplayEffectQueryTarget>("QueryTarget");
            public static readonly OrchestrationExtensionContentField Tag =
                OrchestrationExtensionContentField.Identity("Tag", "gameplay.tag");
            public static readonly OrchestrationExtensionContentField Effect =
                OrchestrationExtensionContentField.Identity("Effect", "gameplay.effect");
            public static readonly OrchestrationExtensionContentField Attribute =
                OrchestrationExtensionContentField.Identity("Attribute", "gameplay.attribute");
            public static readonly OrchestrationExtensionContentField AttributeTarget =
                OrchestrationExtensionContentField.Enum<GameplayAttributeReadTarget>("AttributeTarget");
            public static readonly OrchestrationExtensionContentField DefinitionRevision =
                OrchestrationExtensionContentField.Value("DefinitionRevision", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionContentField SuccessEvent =
                OrchestrationExtensionContentField.Identity("SuccessEvent", "gameplay.action-event");
            public static readonly OrchestrationExtensionContentField SuccessEventTag =
                OrchestrationExtensionContentField.Identity("SuccessEventTag", "gameplay.tag");
            public static readonly OrchestrationExtensionContentField Selector =
                OrchestrationExtensionContentField.Enum<ThirdPersonGameplay.Effects.GameplayEffectRemoveSelector>("Selector");
        }

        public readonly struct HasTag
        {
            static readonly OrchestrationExtensionPorts<HasTag> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<HasTag> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<HasTag> Definition = new OrchestrationExtensionDefinition<HasTag>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-tag-has"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { Fields.QueryTarget, Fields.Tag, GameplayOrchestrationPayloadContract.ProviderOwner }));
        }

        public readonly struct MatchTags
        {
            static readonly OrchestrationExtensionPorts<MatchTags> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<MatchTags> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<MatchTags> Definition = new OrchestrationExtensionDefinition<MatchTags>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-tag-query"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[]
                    {
                        Fields.QueryTarget, GameplayOrchestrationPayloadContract.ProviderOwner,
                        GameplayOrchestrationPayloadContract.QueryAll, GameplayOrchestrationPayloadContract.QueryAny,
                        GameplayOrchestrationPayloadContract.QueryNone
                    }));
        }

        public readonly struct SourceAllowsCandidate
        {
            static readonly OrchestrationExtensionPorts<SourceAllowsCandidate> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<SourceAllowsCandidate> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<SourceAllowsCandidate> Definition = new OrchestrationExtensionDefinition<SourceAllowsCandidate>(
                new OrchestrationExtensionIdentity(DomainId, "source-effects-allow-candidate"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct Active
        {
            static readonly OrchestrationExtensionPorts<Active> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<Active> Result =
                Ports.Output("result", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<Active> Handle =
                Ports.Output("handle", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionOutputPort<Active> EventTag =
                Ports.Output("event-tag", OrchestrationValueKind.Identity);
            public static readonly OrchestrationExtensionOutputPort<Active> EventId =
                Ports.Output("event-id", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionOutputPort<Active> SourceTick =
                Ports.Output("source-tick", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionOutputPort<Active> AcceptedTick =
                Ports.Output("accepted-tick", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionOutputPort<Active> SourceActionId =
                Ports.Output("source-action-id", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionOutputPort<Active> TargetId =
                Ports.Output("target-id", OrchestrationValueKind.Identity);
            public static readonly OrchestrationExtensionOutputPort<Active> Position =
                Ports.Output("position", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionOutputPort<Active> Yaw =
                Ports.Output("yaw", OrchestrationValueKind.Scalar);

            public static readonly OrchestrationExtensionDefinition<Active> Definition = new OrchestrationExtensionDefinition<Active>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-effect-active"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { Fields.QueryTarget, Fields.Effect, GameplayOrchestrationPayloadContract.ProviderOwner }));
        }

        public readonly struct AttributeRead
        {
            static readonly OrchestrationExtensionPorts<AttributeRead> Ports = new();
            public static readonly OrchestrationExtensionInputPort<AttributeRead> ObjectHandle =
                Ports.Input("object-handle", OrchestrationValueKind.ObjectHandle, OrchestrationObjectKind.Any);
            public static readonly OrchestrationExtensionOutputPort<AttributeRead> Valid =
                Ports.Output("valid", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<AttributeRead> BaseValue =
                Ports.Output("base-value", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionOutputPort<AttributeRead> CurrentValue =
                Ports.Output("current-value", OrchestrationValueKind.Scalar);

            public static readonly OrchestrationExtensionDefinition<AttributeRead> Definition = new OrchestrationExtensionDefinition<AttributeRead>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-attribute-read"),
                5, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { Fields.Attribute, Fields.AttributeTarget, GameplayOrchestrationPayloadContract.ProviderOwner }));
        }

        public readonly struct Apply
        {
            static readonly OrchestrationExtensionPorts<Apply> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<Apply> Applied =
                Ports.Output("applied", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<Apply> Handle =
                Ports.Output("handle", OrchestrationValueKind.UInt64);

            public static readonly OrchestrationExtensionDefinition<Apply> Definition = new OrchestrationExtensionDefinition<Apply>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-effect-apply"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[]
                    {
                        Fields.Effect, Fields.DefinitionRevision, GameplayActionExtensionContracts.Fields.ActionContext,
                        Fields.SuccessEvent, Fields.SuccessEventTag, GameplayOrchestrationPayloadContract.ProviderOwner
                    }));
        }

        public readonly struct Remove
        {
            static readonly OrchestrationExtensionPorts<Remove> Ports = new();
            public static readonly OrchestrationExtensionInputPort<Remove> HandleInput =
                Ports.Input("handle", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionOutputPort<Remove> Removed =
                Ports.Output("removed", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionDefinition<Remove> Definition = new OrchestrationExtensionDefinition<Remove>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-effect-remove"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[]
                    {
                        Fields.Selector, Fields.Effect, GameplayOrchestrationPayloadContract.ProviderOwner,
                        GameplayOrchestrationPayloadContract.QueryAll, GameplayOrchestrationPayloadContract.QueryAny,
                        GameplayOrchestrationPayloadContract.QueryNone
                    }));
        }
    }
}
