using System;
using BTSMTL.Orchestration;
using Field = BTSMTL.Orchestration.OrchestrationExtensionContentField;

namespace ThirdPersonGameplay.Orchestration
{
    public enum GameplayTargetDirection : byte { Forward, Up, Down, Left, Right }

    public static partial class GameplayMotionExtensionContracts
    {
        public static class DirectedMotionFields
        {
            public static readonly Field Speed = Field.Value("Speed", OrchestrationValueKind.Scalar);
            public static readonly Field ReleaseRadius = Field.Value("ReleaseRadius", OrchestrationValueKind.Scalar);
            public static readonly Field InheritedSpeedRatio = Field.Value("InheritedSpeedRatio", OrchestrationValueKind.Scalar);
            public static readonly Field BlockedSpeed = Field.Value("BlockedSpeed", OrchestrationValueKind.Scalar);
            public static readonly Field BlockTimeout = Field.Value("BlockTimeout", OrchestrationValueKind.Scalar);
            public static readonly Field TurnSpeed = Field.Value("TurnSpeed", OrchestrationValueKind.Scalar);
            public static readonly ReadOnlyMemory<Field> All = new[]
                { Speed, ReleaseRadius, InheritedSpeedRatio, BlockedSpeed, BlockTimeout, TurnSpeed };
        }

        public readonly struct BeginDirectedMotion
        {
            public static readonly OrchestrationExtensionDefinition<BeginDirectedMotion> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "begin-directed-motion"), 2,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, Array.Empty<OrchestrationExtensionPort>(), DirectedMotionFields.All));
        }

        public readonly struct ReadDirectedMotion
        {
            public static readonly OrchestrationExtensionOutputPort<ReadDirectedMotion> Ended = new("ended", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<ReadDirectedMotion> Reason = new("reason", OrchestrationValueKind.Int32, 1);
            public static readonly OrchestrationExtensionOutputPort<ReadDirectedMotion> Direction = new("direction", OrchestrationValueKind.Vector3, 2);
            public static readonly OrchestrationExtensionOutputPort<ReadDirectedMotion> Grounded = new("grounded", OrchestrationValueKind.Boolean, 3);
            public static readonly OrchestrationExtensionDefinition<ReadDirectedMotion> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "read-directed-motion"), 2,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[] { Ended.Declaration, Reason.Declaration, Direction.Declaration, Grounded.Declaration }, Array.Empty<Field>()));
        }

        public readonly struct TargetDirectionMatches
        {
            public static readonly Field Direction = Field.Enum<GameplayTargetDirection>("Direction");
            public static readonly OrchestrationExtensionOutputPort<TargetDirectionMatches> Result = new("result", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionDefinition<TargetDirectionMatches> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "target-direction-matches"), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate, new[] { Result.Declaration }, new[] { Direction }));
        }

        public readonly struct EndDirectedMotion
        {
            public static readonly OrchestrationExtensionDefinition<EndDirectedMotion> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "end-directed-motion"), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, Array.Empty<OrchestrationExtensionPort>(), Array.Empty<Field>()));
        }
    }
}
