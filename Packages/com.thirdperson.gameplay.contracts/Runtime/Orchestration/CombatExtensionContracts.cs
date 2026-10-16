using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class CombatExtensionContracts
    {
        public const string DomainId = "gameplay.combat";

        public readonly struct AttackQuery
        {
            public static readonly OrchestrationExtensionOutputPort<AttackQuery> Submitted =
                new OrchestrationExtensionOutputPort<AttackQuery>("submitted", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionContentField Attack =
                OrchestrationExtensionContentField.Identity("Attack", "gameplay.attack");

            public static readonly OrchestrationExtensionContentField DefinitionRevision =
                OrchestrationExtensionContentField.Value("DefinitionRevision", OrchestrationValueKind.UInt64);

            public static readonly OrchestrationExtensionContentField AttackEventId =
                OrchestrationExtensionContentField.Identity("AttackEventId", "gameplay.attack-event");

            public static readonly OrchestrationExtensionContentField AttackIntervalEffect =
                OrchestrationExtensionContentField.Identity("AttackIntervalEffect", "gameplay.effect");

            public static readonly OrchestrationExtensionDefinition<AttackQuery> Definition = new OrchestrationExtensionDefinition<AttackQuery>(
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

        public readonly struct AttackHitQuery
        {
            public static readonly OrchestrationExtensionOutputPort<AttackHitQuery> Hit =
                new OrchestrationExtensionOutputPort<AttackHitQuery>("hit", OrchestrationValueKind.Boolean, 0);

            public static readonly OrchestrationExtensionContentField AttackEventId =
                OrchestrationExtensionContentField.Identity("AttackEventId", "gameplay.attack-event");

            public static readonly OrchestrationExtensionDefinition<AttackHitQuery> Definition = new OrchestrationExtensionDefinition<AttackHitQuery>(
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
