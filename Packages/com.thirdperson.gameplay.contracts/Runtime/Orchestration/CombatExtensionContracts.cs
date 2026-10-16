using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class CombatExtensionContracts
    {
        public const string DomainId = "gameplay.combat";

        public static class AttackQuery
        {
            public static readonly OrchestrationExtensionOutputPort Submitted =
                new OrchestrationExtensionOutputPort("submitted", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionContentField Attack =
                OrchestrationExtensionContentField.Identity("Attack", "gameplay.attack");

            public static readonly OrchestrationExtensionContentField DefinitionRevision =
                OrchestrationExtensionContentField.Value("DefinitionRevision", OrchestrationValueKind.UInt64);

            public static readonly OrchestrationExtensionContentField AttackEventId =
                OrchestrationExtensionContentField.Identity("AttackEventId", "gameplay.attack-event");

            public static readonly OrchestrationExtensionContentField AttackIntervalEffect =
                OrchestrationExtensionContentField.Identity("AttackIntervalEffect", "gameplay.effect");

            public static readonly OrchestrationExtensionDefinition Definition = new OrchestrationExtensionDefinition(
                new OrchestrationExtensionIdentity(DomainId, "attack-query"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    new[]
                    {
                        Submitted.Declaration
                    }, new[]
                    {
                        Attack, DefinitionRevision, AttackEventId, AttackIntervalEffect,
                        GameplayOrchestrationPayloadContract.ProviderOwner
                    }));
        }

        public static class AttackHitQuery
        {
            public static readonly OrchestrationExtensionOutputPort Hit =
                new OrchestrationExtensionOutputPort("hit", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionContentField AttackEventId =
                OrchestrationExtensionContentField.Identity("AttackEventId", "gameplay.attack-event");

            public static readonly OrchestrationExtensionDefinition Definition = new OrchestrationExtensionDefinition(
                new OrchestrationExtensionIdentity(DomainId, "attack-hit-query"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        Hit.Declaration
                    }, new[] { AttackEventId }));
        }
    }
}
