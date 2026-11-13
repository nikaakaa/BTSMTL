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
            static readonly OrchestrationExtensionPorts<EndAuthoredTrajectory> Ports = new();
            public static readonly OrchestrationExtensionInputPort<EndAuthoredTrajectory> When = Ports.Input("when", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<EndAuthoredTrajectory> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "end-authored-trajectory"), 2,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, Ports.Seal(),
                    Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct ReadObstacleTraversal
        {
            static readonly OrchestrationExtensionPorts<ReadObstacleTraversal> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> Variant = Ports.Output("variant", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> FrontLedge = Ports.Output("front-ledge", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> BackLedge = Ports.Output("back-ledge", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> BackFloor = Ports.Output("back-floor", OrchestrationValueKind.Vector3);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> Yaw = Ports.Output("yaw", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> Ended = Ports.Output("ended", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> Succeeded = Ports.Output("succeeded", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> Reason = Ports.Output("reason", OrchestrationValueKind.Int32);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> HasTarget = Ports.Output("has-target", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> EntryTime = Ports.Output("entry-time", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> ShouldRelease = Ports.Output("should-release", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<ReadObstacleTraversal> PlaybackRate = Ports.Output("playback-rate", OrchestrationValueKind.Scalar);
            public static readonly OrchestrationExtensionDefinition<ReadObstacleTraversal> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "read-obstacle-traversal"), 3,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate, Ports.Seal(),
                    Array.Empty<OrchestrationExtensionContentField>()));
        }
    }
}
