using System;
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
            public static readonly OrchestrationExtensionOutputPort<SelectTarget> Target =
                new("target", OrchestrationValueKind.ObjectHandle, 0, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<SelectTarget> Found =
                new("found", OrchestrationValueKind.Boolean, 1);
            public static readonly OrchestrationExtensionDefinition<SelectTarget> Definition = Read<SelectTarget>(
                "decision-select-target", new[] { Target.Declaration, Found.Declaration },
                new[] { Fields.Relation, Fields.MaxDistance, Fields.RequireLineOfSight });
        }

        public readonly struct TargetRelation
        {
            public static readonly OrchestrationExtensionInputPort<TargetRelation> Target =
                new("target", OrchestrationValueKind.ObjectHandle, 0, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<TargetRelation> Valid =
                new("valid", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<TargetRelation> Distance =
                new("distance", OrchestrationValueKind.Scalar, 1);
            public static readonly OrchestrationExtensionOutputPort<TargetRelation> Direction =
                new("direction", OrchestrationValueKind.Vector2, 2);
            public static readonly OrchestrationExtensionDefinition<TargetRelation> Definition = Read<TargetRelation>(
                "decision-target-relation",
                new[] { Target.Declaration, Valid.Declaration, Distance.Declaration, Direction.Declaration });
        }

        public readonly struct LineOfSight
        {
            public static readonly OrchestrationExtensionInputPort<LineOfSight> Target =
                new("target", OrchestrationValueKind.ObjectHandle, 0, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<LineOfSight> Visible =
                new("visible", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionDefinition<LineOfSight> Definition = Read<LineOfSight>(
                "decision-line-of-sight", new[] { Target.Declaration, Visible.Declaration });
        }

        public readonly struct CanRequest
        {
            public static readonly OrchestrationExtensionInputPort<CanRequest> Target =
                new("target", OrchestrationValueKind.ObjectHandle, 0, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<CanRequest> Available =
                new("available", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionDefinition<CanRequest> Definition = Read<CanRequest>(
                "decision-can-request", new[] { Target.Declaration, Available.Declaration }, new[] { Fields.Request });
        }

        public readonly struct Attribute
        {
            public static readonly OrchestrationExtensionOutputPort<Attribute> Value =
                new("value", OrchestrationValueKind.Scalar, 0);
            public static readonly OrchestrationExtensionDefinition<Attribute> Definition = Read<Attribute>(
                "decision-attribute", new[] { Value.Declaration }, new[] { Fields.Attribute });
        }

        public readonly struct HasTag
        {
            public static readonly OrchestrationExtensionOutputPort<HasTag> Value =
                new("value", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionDefinition<HasTag> Definition = Read<HasTag>(
                "decision-has-tag", new[] { Value.Declaration }, new[] { Fields.Tag });
        }

        public readonly struct Acting
        {
            public static readonly OrchestrationExtensionOutputPort<Acting> Active =
                new("active", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionDefinition<Acting> Definition = Read<Acting>(
                "decision-acting", new[] { Active.Declaration });
        }

        public readonly struct CurrentTick
        {
            public static readonly OrchestrationExtensionOutputPort<CurrentTick> Tick =
                new("tick", OrchestrationValueKind.UInt64, 0);
            public static readonly OrchestrationExtensionDefinition<CurrentTick> Definition = Read<CurrentTick>(
                "decision-current-tick", new[] { Tick.Declaration });
        }

        public readonly struct RandomInt
        {
            public static readonly OrchestrationExtensionOutputPort<RandomInt> Value =
                new("value", OrchestrationValueKind.Int32, 0);
            public static readonly OrchestrationExtensionDefinition<RandomInt> Definition = Read<RandomInt>(
                "decision-random-int", new[] { Value.Declaration }, new[] { Fields.Minimum, Fields.Maximum });
        }

        public readonly struct Move
        {
            public static readonly OrchestrationExtensionInputPort<Move> Value =
                new("value", OrchestrationValueKind.Vector2, 0);
            public static readonly OrchestrationExtensionDefinition<Move> Definition = Write<Move>(
                "decision-move", new[] { Value.Declaration }, new[] { Fields.Input });
        }

        public readonly struct SubmitRequest
        {
            public static readonly OrchestrationExtensionInputPort<SubmitRequest> Target =
                new("target", OrchestrationValueKind.ObjectHandle, 0, GameplayObjectKinds.Actor);
            public static readonly OrchestrationExtensionOutputPort<SubmitRequest> Submitted =
                new("submitted", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionDefinition<SubmitRequest> Definition = Write<SubmitRequest>(
                "decision-submit-request", new[] { Target.Declaration, Submitted.Declaration }, new[] { Fields.Request });
        }

        static OrchestrationExtensionDefinition<TOperation> Read<TOperation>(string operation,
            OrchestrationExtensionPort[] ports, OrchestrationExtensionContentField[] fields = null)
            where TOperation : struct =>
            new(new OrchestrationExtensionIdentity(DomainId, operation), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value, OrchestrationExtensionAccess.ReadOnly,
                    OrchestrationExtensionInvocationKind.Evaluate, OrchestrationExtensionCompletion.Immediate,
                    ports, fields ?? Array.Empty<OrchestrationExtensionContentField>()));

        static OrchestrationExtensionDefinition<TOperation> Write<TOperation>(string operation,
            OrchestrationExtensionPort[] ports, OrchestrationExtensionContentField[] fields) where TOperation : struct =>
            new(new OrchestrationExtensionIdentity(ActorDomainId, operation), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, ports, fields));
    }
}
