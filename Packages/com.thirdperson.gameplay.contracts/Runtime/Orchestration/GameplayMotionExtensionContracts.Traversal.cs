using System;
using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public enum AuthoredTrajectoryEndReason : byte
    {
        None,
        Completed,
        Released,
        Blocked,
        Interrupted,
        SourceLost,
        TargetLimited,
        ExitNotReached,
        LostSupport
    }

    public static partial class GameplayMotionExtensionContracts
    {
        public readonly struct BeginAuthoredTrajectory
        {
            public static readonly OrchestrationExtensionDefinition<BeginAuthoredTrajectory> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "begin-authored-trajectory"), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, Array.Empty<OrchestrationExtensionPort>(),
                    Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct EndAuthoredTrajectory
        {
            public static readonly OrchestrationExtensionInputPort<EndAuthoredTrajectory> When = new("when", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionDefinition<EndAuthoredTrajectory> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "end-authored-trajectory"), 2,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, new[] { When.Declaration },
                    Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct ReadObstacleTraversal
        {
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> Variant = new("variant", OrchestrationValueKind.Int32, 0);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> FrontLedge = new("front-ledge", OrchestrationValueKind.Vector3, 1);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> BackLedge = new("back-ledge", OrchestrationValueKind.Vector3, 2);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> BackFloor = new("back-floor", OrchestrationValueKind.Vector3, 3);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> Yaw = new("yaw", OrchestrationValueKind.Scalar, 4);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> Ended = new("ended", OrchestrationValueKind.Boolean, 5);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> Succeeded = new("succeeded", OrchestrationValueKind.Boolean, 6);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> Reason = new("reason", OrchestrationValueKind.Int32, 7);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> HasTarget = new("has-target", OrchestrationValueKind.Boolean, 8);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> EntryTime = new("entry-time", OrchestrationValueKind.Scalar, 9);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> ShouldRelease = new("should-release", OrchestrationValueKind.Boolean, 10);
            public static readonly OrchestrationExtensionDefinition<ReadObstacleTraversal> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "read-obstacle-traversal"), 2,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[] { Variant.Declaration, FrontLedge.Declaration, BackLedge.Declaration, BackFloor.Declaration,
                        Yaw.Declaration, Ended.Declaration, Succeeded.Declaration, Reason.Declaration, HasTarget.Declaration,
                        EntryTime.Declaration, ShouldRelease.Declaration },
                    Array.Empty<OrchestrationExtensionContentField>()));
        }
    }
}
