using System;
using BTSMTL.Orchestration;
using Field = BTSMTL.Orchestration.OrchestrationExtensionContentField;

namespace ThirdPersonGameplay.Orchestration
{
    public static partial class GameplayMotionExtensionContracts
    {
        public static class GrappleFields
        {
            public static readonly Field MaximumLength = Field.Value("MaximumLength", OrchestrationValueKind.Scalar);
            public static readonly Field HookSpeed = Field.Value("HookSpeed", OrchestrationValueKind.Scalar);
            public static readonly Field PreparationTime = Field.Value("PreparationTime", OrchestrationValueKind.Scalar);
            public static readonly Field TakeoffDelay = Field.Value("TakeoffDelay", OrchestrationValueKind.Scalar);
            public static readonly Field PullStartSpeed = Field.Value("PullStartSpeed", OrchestrationValueKind.Scalar);
            public static readonly Field PullSpeed = Field.Value("PullSpeed", OrchestrationValueKind.Scalar);
            public static readonly Field PullRampTime = Field.Value("PullRampTime", OrchestrationValueKind.Scalar);
            public static readonly Field PullAcceleration = Field.Value("PullAcceleration", OrchestrationValueKind.Scalar);
            public static readonly Field InitialImpulse = Field.Value("InitialImpulse", OrchestrationValueKind.Scalar);
            public static readonly Field AirAcceleration = Field.Value("AirAcceleration", OrchestrationValueKind.Scalar);
            public static readonly Field AirSteeringAcceleration = Field.Value("AirSteeringAcceleration", OrchestrationValueKind.Scalar);
            public static readonly Field AirSpeed = Field.Value("AirSpeed", OrchestrationValueKind.Scalar);
            public static readonly Field GravityScale = Field.Value("GravityScale", OrchestrationValueKind.Scalar);
            public static readonly Field MinimumGravityScale = Field.Value("MinimumGravityScale", OrchestrationValueKind.Scalar);
            public static readonly Field TangentialDeceleration = Field.Value("TangentialDeceleration", OrchestrationValueKind.Scalar);
            public static readonly Field LaunchHeight = Field.Value("LaunchHeight", OrchestrationValueKind.Scalar);
            public static readonly Field SurfaceMask = Field.Value("SurfaceMask", OrchestrationValueKind.Int32);
            public static readonly Field ReleaseRadius = Field.Value("ReleaseRadius", OrchestrationValueKind.Scalar);
            public static readonly Field BlockedSpeed = Field.Value("BlockedSpeed", OrchestrationValueKind.Scalar);
            public static readonly Field BlockTimeout = Field.Value("BlockTimeout", OrchestrationValueKind.Scalar);
            public static readonly Field TurnSpeed = Field.Value("TurnSpeed", OrchestrationValueKind.Scalar);
            public static readonly ReadOnlyMemory<Field> All = new[]
                { MaximumLength, HookSpeed, PreparationTime, TakeoffDelay, PullStartSpeed, PullSpeed, PullRampTime, PullAcceleration, InitialImpulse, AirAcceleration, AirSteeringAcceleration, AirSpeed,
                    MinimumGravityScale, GravityScale, TangentialDeceleration, LaunchHeight, SurfaceMask,
                    ReleaseRadius, BlockedSpeed, BlockTimeout, TurnSpeed };
        }

        public readonly struct BeginGrapple
        {
            public static readonly OrchestrationExtensionDefinition<BeginGrapple> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "begin-grapple"), 4,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, Array.Empty<OrchestrationExtensionPort>(), GrappleFields.All));
        }

        public readonly struct ReadGrapple
        {
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Ended = new("ended", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Reason = new("reason", OrchestrationValueKind.Int32, 1);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Direction = new("direction", OrchestrationValueKind.Vector3, 2);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Attached = new("attached", OrchestrationValueKind.Boolean, 3);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> InFlight = new("in-flight", OrchestrationValueKind.Boolean, 4);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> GroundHandoff = new("ground-handoff", OrchestrationValueKind.Boolean, 5);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Steady = new("steady", OrchestrationValueKind.Boolean, 6);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Active = new("active", OrchestrationValueKind.Boolean, 7);
            public static readonly OrchestrationExtensionOutputPort<ReadGrapple> Approaching = new("approaching", OrchestrationValueKind.Boolean, 8);
            public static readonly OrchestrationExtensionDefinition<ReadGrapple> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "read-grapple"), 4,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate,
                    new[] { Ended.Declaration, Reason.Declaration, Direction.Declaration, Attached.Declaration,
                        InFlight.Declaration, GroundHandoff.Declaration, Steady.Declaration, Active.Declaration,
                        Approaching.Declaration }, Array.Empty<Field>()));
        }

        public readonly struct EndGrapple
        {
            public static readonly OrchestrationExtensionInputPort<EndGrapple> Requested = new("requested", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionDefinition<EndGrapple> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "end-grapple"), 2,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, new[] { Requested.Declaration }, Array.Empty<Field>()));
        }
    }
}
