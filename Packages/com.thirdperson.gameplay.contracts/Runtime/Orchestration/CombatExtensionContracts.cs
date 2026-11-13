using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class CombatExtensionContracts
    {
        public const string DomainId = "gameplay.combat";

        public readonly struct AttackQuery
        {
            static readonly OrchestrationExtensionPorts<AttackQuery> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<AttackQuery> Submitted =
                Ports.Output("submitted", OrchestrationValueKind.Boolean);

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
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[]
                    {
                        Attack, DefinitionRevision, AttackEventId, AttackIntervalEffect,
                        GameplayOrchestrationPayloadContract.ProviderOwner
                    }));
        }

        public readonly struct AttackHitQuery
        {
            static readonly OrchestrationExtensionPorts<AttackHitQuery> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<AttackHitQuery> Hit =
                Ports.Output("hit", OrchestrationValueKind.Boolean);

            public static readonly OrchestrationExtensionContentField AttackEventId =
                OrchestrationExtensionContentField.Identity("AttackEventId", "gameplay.attack-event");

            public static readonly OrchestrationExtensionDefinition<AttackHitQuery> Definition = new OrchestrationExtensionDefinition<AttackHitQuery>(
                new OrchestrationExtensionIdentity(DomainId, "attack-hit-query"),
                4, new OrchestrationExtensionContract(OrchestrationExtensionKind.Condition,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { AttackEventId }));
        }
    }
}
