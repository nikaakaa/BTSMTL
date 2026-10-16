using System;
using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayEffectExtensionContracts
    {
        public const string DomainId = "gameplay.effects";

        public static class Ports
        {
            public static readonly OrchestrationExtensionOutputPort Result =
                new OrchestrationExtensionOutputPort("result", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort Handle =
                new OrchestrationExtensionOutputPort("handle", OrchestrationValueKind.UInt64, 1);
            public static readonly OrchestrationExtensionOutputPort EventTag =
                new OrchestrationExtensionOutputPort("event-tag", OrchestrationValueKind.Identity, 2);
            public static readonly OrchestrationExtensionOutputPort EventId =
                new OrchestrationExtensionOutputPort("event-id", OrchestrationValueKind.UInt64, 3);
            public static readonly OrchestrationExtensionOutputPort SourceTick =
                new OrchestrationExtensionOutputPort("source-tick", OrchestrationValueKind.UInt64, 4);
            public static readonly OrchestrationExtensionOutputPort AcceptedTick =
                new OrchestrationExtensionOutputPort("accepted-tick", OrchestrationValueKind.UInt64, 5);
            public static readonly OrchestrationExtensionOutputPort SourceActionId =
                new OrchestrationExtensionOutputPort("source-action-id", OrchestrationValueKind.UInt64, 6);
            public static readonly OrchestrationExtensionOutputPort TargetId =
                new OrchestrationExtensionOutputPort("target-id", OrchestrationValueKind.Identity, 7);
            public static readonly OrchestrationExtensionOutputPort Position =
                new OrchestrationExtensionOutputPort("position", OrchestrationValueKind.Vector3, 8);
            public static readonly OrchestrationExtensionOutputPort Yaw =
                new OrchestrationExtensionOutputPort("yaw", OrchestrationValueKind.Scalar, 9);
            public static readonly OrchestrationExtensionOutputPort Valid =
                new OrchestrationExtensionOutputPort("valid", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort BaseValue =
                new OrchestrationExtensionOutputPort("base-value", OrchestrationValueKind.Scalar, 1);
            public static readonly OrchestrationExtensionOutputPort CurrentValue =
                new OrchestrationExtensionOutputPort("current-value", OrchestrationValueKind.Scalar, 2);
            public static readonly OrchestrationExtensionOutputPort Applied =
                new OrchestrationExtensionOutputPort("applied", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionInputPort HandleInput =
                new OrchestrationExtensionInputPort("handle", OrchestrationValueKind.UInt64, 0);
            public static readonly OrchestrationExtensionOutputPort Removed =
                new OrchestrationExtensionOutputPort("removed", OrchestrationValueKind.Boolean, 0);
        }

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

        public static readonly OrchestrationExtensionDefinition HasTag = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "gameplay-tag-has"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Result.Declaration
                }, new[] { Fields.QueryTarget, Fields.Tag, GameplayOrchestrationPayloadContract.ProviderOwner }));

        public static readonly OrchestrationExtensionDefinition MatchTags = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "gameplay-tag-query"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Result.Declaration
                }, new[]
                {
                    Fields.QueryTarget, GameplayOrchestrationPayloadContract.ProviderOwner,
                    GameplayOrchestrationPayloadContract.QueryAll, GameplayOrchestrationPayloadContract.QueryAny,
                    GameplayOrchestrationPayloadContract.QueryNone
                }));

        public static readonly OrchestrationExtensionDefinition SourceAllowsCandidate = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "source-effects-allow-candidate"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Result.Declaration
                }, Array.Empty<OrchestrationExtensionContentField>()));

        public static readonly OrchestrationExtensionDefinition Active = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "gameplay-effect-active"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Result.Declaration,
                    Ports.Handle.Declaration,
                    Ports.EventTag.Declaration,
                    Ports.EventId.Declaration,
                    Ports.SourceTick.Declaration,
                    Ports.AcceptedTick.Declaration,
                    Ports.SourceActionId.Declaration,
                    Ports.TargetId.Declaration,
                    Ports.Position.Declaration,
                    Ports.Yaw.Declaration
                }, new[] { Fields.QueryTarget, Fields.Effect, GameplayOrchestrationPayloadContract.ProviderOwner }));

        public static readonly OrchestrationExtensionDefinition AttributeRead = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "gameplay-attribute-read"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.Valid.Declaration,
                    Ports.BaseValue.Declaration,
                    Ports.CurrentValue.Declaration
                }, new[] { Fields.Attribute, GameplayOrchestrationPayloadContract.ProviderOwner }));

        public static readonly OrchestrationExtensionDefinition Apply = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "gameplay-effect-apply"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    Ports.Applied.Declaration,
                    Ports.Handle.Declaration
                }, new[]
                {
                    Fields.Effect, Fields.DefinitionRevision, GameplayActionExtensionContracts.Fields.ActionContext,
                    Fields.SuccessEvent, Fields.SuccessEventTag, GameplayOrchestrationPayloadContract.ProviderOwner
                }));

        public static readonly OrchestrationExtensionDefinition Remove = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "gameplay-effect-remove"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    Ports.HandleInput.Declaration,
                    Ports.Removed.Declaration
                }, new[]
                {
                    Fields.Selector, Fields.Effect, GameplayOrchestrationPayloadContract.ProviderOwner,
                    GameplayOrchestrationPayloadContract.QueryAll, GameplayOrchestrationPayloadContract.QueryAny,
                    GameplayOrchestrationPayloadContract.QueryNone
                }));
    }
}
