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
            static readonly OrchestrationExtensionPorts<ReadGroundTakeoff> Ports = new();
            public static readonly OrchestrationExtensionOutputPort<ReadGroundTakeoff> Applied = Ports.Output("applied", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionOutputPort<ReadGroundTakeoff> Rejected = Ports.Output("rejected", OrchestrationValueKind.Boolean);
            public static readonly OrchestrationExtensionDefinition<ReadGroundTakeoff> Definition = new(
                new OrchestrationExtensionIdentity(DomainId, "read-ground-takeoff"), 1,
                new OrchestrationExtensionContract(OrchestrationExtensionKind.Value,
                    OrchestrationExtensionAccess.ReadOnly, OrchestrationExtensionInvocationKind.Evaluate,
                    OrchestrationExtensionCompletion.Immediate, Ports.Seal(),
                    Array.Empty<OrchestrationExtensionContentField>()));
        }
    }
}
