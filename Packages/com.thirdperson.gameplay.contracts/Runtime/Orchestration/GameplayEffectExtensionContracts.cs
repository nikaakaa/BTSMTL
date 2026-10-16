using System;
using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
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
            public static readonly OrchestrationExtensionOutputPort<HasTag> Result =
                new OrchestrationExtensionOutputPort<HasTag>("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<HasTag> Definition = new OrchestrationExtensionDefinition<HasTag>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-tag-has"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Result.Declaration
                    }, new[] { Fields.QueryTarget, Fields.Tag, GameplayOrchestrationPayloadContract.ProviderOwner }));
        }

        public readonly struct MatchTags
        {
            public static readonly OrchestrationExtensionOutputPort<MatchTags> Result =
                new OrchestrationExtensionOutputPort<MatchTags>("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<MatchTags> Definition = new OrchestrationExtensionDefinition<MatchTags>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-tag-query"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Result.Declaration
                    }, new[]
                    {
                        Fields.QueryTarget, GameplayOrchestrationPayloadContract.ProviderOwner,
                        GameplayOrchestrationPayloadContract.QueryAll, GameplayOrchestrationPayloadContract.QueryAny,
                        GameplayOrchestrationPayloadContract.QueryNone
                    }));
        }

        public readonly struct SourceAllowsCandidate
        {
            public static readonly OrchestrationExtensionOutputPort<SourceAllowsCandidate> Result =
                new OrchestrationExtensionOutputPort<SourceAllowsCandidate>("result", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<SourceAllowsCandidate> Definition = new OrchestrationExtensionDefinition<SourceAllowsCandidate>(
                new OrchestrationExtensionIdentity(DomainId, "source-effects-allow-candidate"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Result.Declaration
                    }, Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct Active
        {
            public static readonly OrchestrationExtensionOutputPort<Active> Result =
                new OrchestrationExtensionOutputPort<Active>("result", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<Active> Handle =
                new OrchestrationExtensionOutputPort<Active>("handle", OrchestrationValueKind.UInt64, 1);
            public static readonly OrchestrationExtensionOutputPort<Active> EventTag =
                new OrchestrationExtensionOutputPort<Active>("event-tag", OrchestrationValueKind.Identity, 2);
            public static readonly OrchestrationExtensionOutputPort<Active> EventId =
                new OrchestrationExtensionOutputPort<Active>("event-id", OrchestrationValueKind.UInt64, 3);
            public static readonly OrchestrationExtensionOutputPort<Active> SourceTick =
                new OrchestrationExtensionOutputPort<Active>("source-tick", OrchestrationValueKind.UInt64, 4);
            public static readonly OrchestrationExtensionOutputPort<Active> AcceptedTick =
                new OrchestrationExtensionOutputPort<Active>("accepted-tick", OrchestrationValueKind.UInt64, 5);
            public static readonly OrchestrationExtensionOutputPort<Active> SourceActionId =
                new OrchestrationExtensionOutputPort<Active>("source-action-id", OrchestrationValueKind.UInt64, 6);
            public static readonly OrchestrationExtensionOutputPort<Active> TargetId =
                new OrchestrationExtensionOutputPort<Active>("target-id", OrchestrationValueKind.Identity, 7);
            public static readonly OrchestrationExtensionOutputPort<Active> Position =
                new OrchestrationExtensionOutputPort<Active>("position", OrchestrationValueKind.Vector3, 8);
            public static readonly OrchestrationExtensionOutputPort<Active> Yaw =
                new OrchestrationExtensionOutputPort<Active>("yaw", OrchestrationValueKind.Scalar, 9);

            public static readonly OrchestrationExtensionDefinition<Active> Definition = new OrchestrationExtensionDefinition<Active>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-effect-active"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Result.Declaration,
                        Handle.Declaration,
                        EventTag.Declaration,
                        EventId.Declaration,
                        SourceTick.Declaration,
                        AcceptedTick.Declaration,
                        SourceActionId.Declaration,
                        TargetId.Declaration,
                        Position.Declaration,
                        Yaw.Declaration
                    }, new[] { Fields.QueryTarget, Fields.Effect, GameplayOrchestrationPayloadContract.ProviderOwner }));
        }

        public readonly struct AttributeRead
        {
            public static readonly OrchestrationExtensionOutputPort<AttributeRead> Valid =
                new OrchestrationExtensionOutputPort<AttributeRead>("valid", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<AttributeRead> BaseValue =
                new OrchestrationExtensionOutputPort<AttributeRead>("base-value", OrchestrationValueKind.Scalar, 1);
            public static readonly OrchestrationExtensionOutputPort<AttributeRead> CurrentValue =
                new OrchestrationExtensionOutputPort<AttributeRead>("current-value", OrchestrationValueKind.Scalar, 2);

            public static readonly OrchestrationExtensionDefinition<AttributeRead> Definition = new OrchestrationExtensionDefinition<AttributeRead>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-attribute-read"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Valid.Declaration,
                        BaseValue.Declaration,
                        CurrentValue.Declaration
                    }, new[] { Fields.Attribute, GameplayOrchestrationPayloadContract.ProviderOwner }));
        }

        public readonly struct Apply
        {
            public static readonly OrchestrationExtensionOutputPort<Apply> Applied =
                new OrchestrationExtensionOutputPort<Apply>("applied", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<Apply> Handle =
                new OrchestrationExtensionOutputPort<Apply>("handle", OrchestrationValueKind.UInt64, 1);

            public static readonly OrchestrationExtensionDefinition<Apply> Definition = new OrchestrationExtensionDefinition<Apply>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-effect-apply"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    new[]
                    {
                        Applied.Declaration,
                        Handle.Declaration
                    }, new[]
                    {
                        Fields.Effect, Fields.DefinitionRevision, GameplayActionExtensionContracts.Fields.ActionContext,
                        Fields.SuccessEvent, Fields.SuccessEventTag, GameplayOrchestrationPayloadContract.ProviderOwner
                    }));
        }

        public readonly struct Remove
        {
            public static readonly OrchestrationExtensionInputPort<Remove> HandleInput =
                new OrchestrationExtensionInputPort<Remove>("handle", OrchestrationValueKind.UInt64, 0);
            public static readonly OrchestrationExtensionOutputPort<Remove> Removed =
                new OrchestrationExtensionOutputPort<Remove>("removed", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionDefinition<Remove> Definition = new OrchestrationExtensionDefinition<Remove>(
                new OrchestrationExtensionIdentity(DomainId, "gameplay-effect-remove"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    new[]
                    {
                        HandleInput.Declaration,
                        Removed.Declaration
                    }, new[]
                    {
                        Fields.Selector, Fields.Effect, GameplayOrchestrationPayloadContract.ProviderOwner,
                        GameplayOrchestrationPayloadContract.QueryAll, GameplayOrchestrationPayloadContract.QueryAny,
                        GameplayOrchestrationPayloadContract.QueryNone
                    }));
        }
    }
}
