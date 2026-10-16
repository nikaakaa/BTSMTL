using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public static class GameplayActionTargetExtensionContracts
    {
        public const string DomainId = "gameplay.actions";

        public static class Ports
        {
            public static readonly OrchestrationExtensionInputPort TargetIdInput =
                new OrchestrationExtensionInputPort("target-id", OrchestrationValueKind.Identity, 0);
            public static readonly OrchestrationExtensionInputPort PositionInput =
                new OrchestrationExtensionInputPort("position", OrchestrationValueKind.Vector3, 1);
            public static readonly OrchestrationExtensionInputPort YawInput =
                new OrchestrationExtensionInputPort("yaw", OrchestrationValueKind.Scalar, 2);
            public static readonly OrchestrationExtensionOutputPort HasTarget =
                new OrchestrationExtensionOutputPort("has-target", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort TargetId =
                new OrchestrationExtensionOutputPort("target-id", OrchestrationValueKind.Identity, 0);
            public static readonly OrchestrationExtensionOutputPort Position =
                new OrchestrationExtensionOutputPort("position", OrchestrationValueKind.Vector3, 1);
            public static readonly OrchestrationExtensionOutputPort Yaw =
                new OrchestrationExtensionOutputPort("yaw", OrchestrationValueKind.Scalar, 2);
            public static readonly OrchestrationExtensionOutputPort PlanarDistance =
                new OrchestrationExtensionOutputPort("planar-distance", OrchestrationValueKind.Scalar, 3);
        }

        public static class Fields
        {
            public static readonly OrchestrationExtensionContentField DeclarationId =
                OrchestrationExtensionContentField.Identity("DeclarationId", "btsmtl.blackboard-declaration");
            public static readonly OrchestrationExtensionContentField DeclarationOwner =
                OrchestrationExtensionContentField.Identity("DeclarationOwner", "btsmtl.blackboard-owner");
        }

        public static readonly OrchestrationExtensionDefinition Read = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "action-target"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                new[]
                {
                    Ports.TargetIdInput.Declaration,
                    Ports.PositionInput.Declaration,
                    Ports.YawInput.Declaration,
                    Ports.HasTarget.Declaration,
                    Ports.Position.Declaration,
                    Ports.Yaw.Declaration,
                    Ports.PlanarDistance.Declaration
                }, new[] { Fields.DeclarationId, Fields.DeclarationOwner }));

        public static readonly OrchestrationExtensionDefinition Capture = new OrchestrationExtensionDefinition(
            new OrchestrationExtensionIdentity(DomainId, "capture-action-target"),
            3, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                new[]
                {
                    Ports.TargetId.Declaration,
                    Ports.Position.Declaration,
                    Ports.Yaw.Declaration
                }, new[] { Fields.DeclarationId, Fields.DeclarationOwner }));
    }
}
