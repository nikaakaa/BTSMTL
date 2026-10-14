using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.FPGameplay
{
    public static class FPGameplayAbilityStateSchema
    {
        public static string CodecIdentity(ProgramStateValueKind kind)
        {
            return kind switch
            {
                ProgramStateValueKind.Boolean => "state.quantum-fp-q48.16-boolean/v1",
                ProgramStateValueKind.Int32 => "state.quantum-fp-q48.16-int32/v1",
                ProgramStateValueKind.UInt64 => "state.quantum-fp-q48.16-uint64/v1",
                ProgramStateValueKind.Scalar => "state.quantum-fp-q48.16-scalar/v1",
                ProgramStateValueKind.Vector2 => "state.quantum-fp-q48.16-vector2/v1",
                ProgramStateValueKind.Vector3 => "state.quantum-fp-q48.16-vector3/v1",
                ProgramStateValueKind.Yaw => "state.quantum-fp-q48.16-yaw/v1",
                ProgramStateValueKind.Identity => "state.quantum-fp-q48.16-identity/v1",
                ProgramStateValueKind.BlackboardOwnerToken => "state.quantum-fp-q48.16-blackboard-owner-token/v1",
                ProgramStateValueKind.BlackboardWriteStamp => "state.quantum-fp-q48.16-blackboard-write-stamp/v2",
                ProgramStateValueKind.ActionTargetSnapshot => "state.quantum-fp-q48.16-action-target-snapshot/v1",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        public static void RequireCodec(ProgramStateSlot slot)
        {
            if (slot == null)
                throw new ArgumentNullException(nameof(slot));
            string expected = CodecIdentity(slot.ValueKind);
            if (!string.Equals(slot.StateCodecIdentity, expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"FP state slot '{slot.Identity}' requires codec '{expected}', got '{slot.StateCodecIdentity}'.");
        }
    }
}
