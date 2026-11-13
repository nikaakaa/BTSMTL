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
            static readonly OrchestrationExtensionPorts<Read> Ports = new();
            public static readonly OrchestrationExtensionInputPort<Read> TargetIdInput =
                Ports.Input("target-id", OrchestrationValueKind.Identity);
            public static readonly OrchestrationExtensionInputPort<Read> PositionInput =
                Ports.Input("position", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionInputPort<Read> YawInput =
                Ports.Input("yaw", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionInputPort<Read> ObjectHandleInput =
                Ports.Input("object-handle", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<Read> HasTarget =
                Ports.Output("has-target", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<Read> Position =
                Ports.Output("position", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionOutputPort<Read> Yaw =
                Ports.Output("yaw", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionOutputPort<Read> PlanarDistance =
                Ports.Output("planar-distance", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionOutputPort<Read> ObjectHandle =
                Ports.Output("object-handle", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);

            public static readonly OrchestrationExtensionDefinition<Read> Definition = new OrchestrationExtensionDefinition<Read>(
                new OrchestrationExtensionIdentity(DomainId, "action-target"),
                5, new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { Fields.DeclarationId, Fields.DeclarationOwner }));
        }

        public readonly struct Capture
        {
            static readonly OrchestrationExtensionPorts<Capture> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<Capture> TargetId =
                Ports.Output("target-id", OrchestrationValueKind.Identity);
            public static readonly OrchestrationExtensionOutputPort<Capture> Position =
                Ports.Output("position", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionOutputPort<Capture> Yaw =
                Ports.Output("yaw", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionOutputPort<Capture> ObjectHandle =
                Ports.Output("object-handle", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);

            public static readonly OrchestrationExtensionDefinition<Capture> Definition = new OrchestrationExtensionDefinition<Capture>(
                new OrchestrationExtensionIdentity(DomainId, "capture-action-target"),
                5, new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate,
                    Ports.Seal(), new[] { Fields.DeclarationId, Fields.DeclarationOwner }));
        }
    }
}
