using System;
using ThirdPersonSimulation;

namespace ThirdPersonSimulation.Fixed
{
    public static class FixedGameplayAbilityStateSchema
    {
        public static string CodecIdentity(ProgramStateValueKind kind)
        {
            return kind switch
            {
                ProgramStateValueKind.Boolean => "state.fixed-q32.32-boolean/v1",
                ProgramStateValueKind.Int32 => "state.fixed-q32.32-int32/v1",
                ProgramStateValueKind.UInt64 => "state.fixed-q32.32-uint64/v1",
                ProgramStateValueKind.Scalar => "state.fixed-q32.32-scalar/v1",
                ProgramStateValueKind.Vector2 => "state.fixed-q32.32-vector2/v1",
                ProgramStateValueKind.Vector3 => "state.fixed-q32.32-vector3/v1",
                ProgramStateValueKind.Yaw => "state.fixed-q32.32-yaw/v1",
                ProgramStateValueKind.Identity => "state.fixed-q32.32-identity/v1",
                ProgramStateValueKind.BlackboardOwnerToken => "state.fixed-q32.32-blackboard-owner-token/v1",
                ProgramStateValueKind.BlackboardWriteStamp => "state.fixed-q32.32-blackboard-write-stamp/v2",
                ProgramStateValueKind.ActionTargetSnapshot => "state.fixed-q32.32-action-target-snapshot/v1",
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
            };
        }

        public static void RequireCodec(ProgramStateSlot slot)
        {
            if (slot == null)
                throw new ArgumentNullException(nameof(slot));
            string expected = CodecIdentity(slot.ValueKind);
            if (!string.Equals(slot.StateCodecIdentity, expected, StringComparison.Ordinal))
                throw new InvalidOperationException($"Fixed state slot '{slot.Identity}' requires codec '{expected}', got '{slot.StateCodecIdentity}'.");
        }
    }
}
