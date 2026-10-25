using System;
using BTSMTL.Orchestration;

namespace ThirdPersonGameplay.Orchestration
{
    public enum GroundTakeoffResult : byte
    {
        None,
        Requested,
        Applied,
        LostSupport,
        Interrupted
    }

    public static partial class GameplayMotionExtensionContracts
    {
        public readonly struct GroundTakeoff
        {
            public static readonly OrchestrationExtensionDefinition<GroundTakeoff> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "ground-takeoff"), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Action,
                    OrchestrationExtensionAccess.CandidateWrite, OrchestrationExtensionInvocationKind.Execute,
                    OrchestrationExtensionCompletion.Immediate, Array.Empty<OrchestrationExtensionPort>(),
                    Array.Empty<OrchestrationExtensionContentField>()));
        }

        public readonly struct ReadGroundTakeoff
        {
            public static readonly OrchestrationExtensionOutputPort<ReadGroundTakeoff> Applied = new("applied", OrchestrationValueKind.Boolean, 0);
            public static readonly OrchestrationExtensionOutputPort<ReadGroundTakeoff> Rejected = new("rejected", OrchestrationValueKind.Boolean, 1);
            public static readonly OrchestrationExtensionDefinition<ReadGroundTakeoff> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "read-ground-takeoff"), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate, new[] { Applied.Declaration, Rejected.Declaration },
                    Array.Empty<OrchestrationExtensionContentField>()));
        }
    }
}
