using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public enum GameplayDecisionTargetRelation : byte
    {
        Hostile = 1,
        Allied = 2,
        Any = 3
    }

    // 决策图只准入两个领域：只读查询与个体输出；输出只写拥有者的控制器输入。
    public static class GameplayDecisionExtensionContracts
    {
        public const string DomainId = "gameplay.decision";
        public const string ActorDomainId = "gameplay.decision.actor";

        public static class Fields
        {
            public static readonly OrchestrationExtensionContentField Relation =
                OrchestrationExtensionContentField.Enum<GameplayDecisionTargetRelation>("Relation");
            public static readonly OrchestrationExtensionContentField MaxDistance =
                OrchestrationExtensionContentField.Value("MaxDistance", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionContentField RequireLineOfSight =
                OrchestrationExtensionContentField.Value("RequireLineOfSight", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionContentField Request =
                OrchestrationExtensionContentField.Text("Request");
            public static readonly OrchestrationExtensionContentField Input =
                OrchestrationExtensionContentField.Text("Input");
            public static readonly OrchestrationExtensionContentField Attribute =
                OrchestrationExtensionContentField.Identity("Attribute", "gameplay.attribute");
            public static readonly OrchestrationExtensionContentField Tag =
                OrchestrationExtensionContentField.Identity("Tag", "gameplay.tag");
            public static readonly OrchestrationExtensionContentField Minimum =
                OrchestrationExtensionContentField.Value("Minimum", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionContentField Maximum =
                OrchestrationExtensionContentField.Value("Maximum", OrchestrationValueKind.Int32);
        }

        public readonly struct SelectTarget
        {
            static readonly OrchestrationExtensionPorts<SelectTarget> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<SelectTarget> Target =
                Ports.Output("target", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<SelectTarget> Found =
                Ports.Output("found", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<SelectTarget> Definition = Read(
                "decision-select-target", Ports, Fields.Relation, Fields.MaxDistance, Fields.RequireLineOfSight);
        }

        public readonly struct TargetRelation
        {
            static readonly OrchestrationExtensionPorts<TargetRelation> Ports = new();
            public static readonly OrchestrationExtensionInputPort<TargetRelation> Target =
                Ports.Input("target", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<TargetRelation> Valid =
                Ports.Output("valid", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<TargetRelation> Distance =
                Ports.Output("distance", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionOutputPort<TargetRelation> Direction =
                Ports.Output("direction", OrchestrationValueKind.Vector2);
            public static readonly OrchestrationExtensionDefinition<TargetRelation> Definition = Read(
                "decision-target-relation", Ports);
        }

        public readonly struct LineOfSight
        {
            static readonly OrchestrationExtensionPorts<LineOfSight> Ports = new();
            public static readonly OrchestrationExtensionInputPort<LineOfSight> Target =
                Ports.Input("target", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<LineOfSight> Visible =
                Ports.Output("visible", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<LineOfSight> Definition = Read(
                "decision-line-of-sight", Ports);
        }

        public readonly struct CanRequest
        {
            static readonly OrchestrationExtensionPorts<CanRequest> Ports = new();
            public static readonly OrchestrationExtensionInputPort<CanRequest> Target =
                Ports.Input("target", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<CanRequest> Available =
                Ports.Output("available", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<CanRequest> Definition = Read(
                "decision-can-request", Ports, Fields.Request);
        }

        public readonly struct Attribute
        {
            static readonly OrchestrationExtensionPorts<Attribute> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<Attribute> Value =
                Ports.Output("value", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionDefinition<Attribute> Definition = Read(
                "decision-attribute", Ports, Fields.Attribute);
        }

        public readonly struct HasTag
        {
            static readonly OrchestrationExtensionPorts<HasTag> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<HasTag> Value =
                Ports.Output("value", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<HasTag> Definition = Read(
                "decision-has-tag", Ports, Fields.Tag);
        }

        public readonly struct Acting
        {
            static readonly OrchestrationExtensionPorts<Acting> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<Acting> Active =
                Ports.Output("active", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<Acting> Definition = Read(
                "decision-acting", Ports);
        }

        public readonly struct CurrentTick
        {
            static readonly OrchestrationExtensionPorts<CurrentTick> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<CurrentTick> Tick =
                Ports.Output("tick", OrchestrationValueKind.UInt64);
            public static readonly OrchestrationExtensionDefinition<CurrentTick> Definition = Read(
                "decision-current-tick", Ports);
        }

        public readonly struct RandomInt
        {
            static readonly OrchestrationExtensionPorts<RandomInt> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<RandomInt> Value =
                Ports.Output("value", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionDefinition<RandomInt> Definition = Read(
                "decision-random-int", Ports, Fields.Minimum, Fields.Maximum);
        }

        public readonly struct Move
        {
            static readonly OrchestrationExtensionPorts<Move> Ports = new();
            public static readonly OrchestrationExtensionInputPort<Move> Value =
                Ports.Input("value", OrchestrationValueKind.Vector2);
            public static readonly OrchestrationExtensionDefinition<Move> Definition = Write(
                "decision-move", Ports, Fields.Input);
        }

        public readonly struct SubmitRequest
        {
            static readonly OrchestrationExtensionPorts<SubmitRequest> Ports = new();
            public static readonly OrchestrationExtensionInputPort<SubmitRequest> Target =
                Ports.Input("target", OrchestrationValueKind.ObjectHandle, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<SubmitRequest> Submitted =
                Ports.Output("submitted", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<SubmitRequest> Definition = Write(
                "decision-submit-request", Ports, Fields.Request);
        }

        static OrchestrationExtensionDefinition<TOperation> Read<TOperation>(string operation,
            OrchestrationExtensionPorts<TOperation> ports, params OrchestrationExtensionContentField[] fields)
            where TOperation : struct =>
            new(new OrchestrationExtensionIdentity(DomainId, operation), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value, OrchestrationExtensionAccess.ReadOnly,
                    OrchestrationExtensionInvocationKind.Evaluate, OrchestrationExtensionCompletion.Immediate,
                    ports.Seal(), fields));

        static OrchestrationExtensionDefinition<TOperation> Write<TOperation>(string operation,
            OrchestrationExtensionPorts<TOperation> ports, params OrchestrationExtensionContentField[] fields)
            where TOperation : struct =>
            new(new OrchestrationExtensionIdentity(ActorDomainId, operation), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, ports.Seal(), fields));
    }
}
