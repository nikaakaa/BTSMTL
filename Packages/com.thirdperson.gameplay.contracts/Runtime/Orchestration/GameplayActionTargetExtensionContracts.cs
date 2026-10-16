using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayActionTargetExtensionContracts
    {
        public const string DomainId = "gameplay.actions";

        public static class Fields
        {
            public static readonly OrchestrationExtensionContentField DeclarationId =
                OrchestrationExtensionContentField.Identity("DeclarationId", "btsmtl.blackboard-declaration");
            public static readonly OrchestrationExtensionContentField DeclarationOwner =
                OrchestrationExtensionContentField.Identity("DeclarationOwner", "btsmtl.blackboard-owner");
        }

        public readonly struct Read
        {
            public static readonly OrchestrationExtensionInputPort<Read> TargetIdInput =
                new OrchestrationExtensionInputPort<Read>("target-id", OrchestrationValueKind.Identity, 0);
            public static readonly OrchestrationExtensionInputPort<Read> PositionInput =
                new OrchestrationExtensionInputPort<Read>("position", OrchestrationValueKind.Vector3, 1);
            public static readonly OrchestrationExtensionInputPort<Read> YawInput =
                new OrchestrationExtensionInputPort<Read>("yaw", OrchestrationValueKind.Scalar, 2);
            public static readonly OrchestrationExtensionOutputPort<Read> HasTarget =
                new OrchestrationExtensionOutputPort<Read>("has-target", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<Read> Position =
                new OrchestrationExtensionOutputPort<Read>("position", OrchestrationValueKind.Vector3, 1);
            public static readonly OrchestrationExtensionOutputPort<Read> Yaw =
                new OrchestrationExtensionOutputPort<Read>("yaw", OrchestrationValueKind.Scalar, 2);
            public static readonly OrchestrationExtensionOutputPort<Read> PlanarDistance =
                new OrchestrationExtensionOutputPort<Read>("planar-distance", OrchestrationValueKind.Scalar, 3);

            public static readonly OrchestrationExtensionDefinition<Read> Definition = new OrchestrationExtensionDefinition<Read>(
                new OrchestrationExtensionIdentity(DomainId, "action-target"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    new[]
                    {
                        TargetIdInput.Declaration,
                        PositionInput.Declaration,
                        YawInput.Declaration,
                        HasTarget.Declaration,
                        Position.Declaration,
                        Yaw.Declaration,
                        PlanarDistance.Declaration
                    }, new[] { Fields.DeclarationId, Fields.DeclarationOwner }));
        }

        public readonly struct Capture
        {
            public static readonly OrchestrationExtensionOutputPort<Capture> TargetId =
                new OrchestrationExtensionOutputPort<Capture>("target-id", OrchestrationValueKind.Identity, 0);
            public static readonly OrchestrationExtensionOutputPort<Capture> Position =
                new OrchestrationExtensionOutputPort<Capture>("position", OrchestrationValueKind.Vector3, 1);
            public static readonly OrchestrationExtensionOutputPort<Capture> Yaw =
                new OrchestrationExtensionOutputPort<Capture>("yaw", OrchestrationValueKind.Scalar, 2);

            public static readonly OrchestrationExtensionDefinition<Capture> Definition = new OrchestrationExtensionDefinition<Capture>(
                new OrchestrationExtensionIdentity(DomainId, "capture-action-target"),
                3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    new[]
                    {
                        TargetId.Declaration,
                        Position.Declaration,
                        Yaw.Declaration
                    }, new[] { Fields.DeclarationId, Fields.DeclarationOwner }));
        }
    }
}
