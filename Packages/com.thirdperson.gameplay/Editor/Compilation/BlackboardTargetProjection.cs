using System;
using System.IO;

namespace ThirdPersonSimulation
{
    public enum ProgramBlackboardInputProjection : byte
    {
        Direct,
        ActionTargetIdentity,
        ActionTargetPosition,
        ActionTargetYaw
    }

    public static class ProgramBlackboardInputProjectionTypes
    {
        public const ProgramInputValueKind TargetInputKind = ProgramInputValueKind.ActionTargetSnapshot;

        public static ProgramStateValueKind StateKind(ProgramInputValueKind kind, ProgramBlackboardInputProjection projection)
        {
            if (projection != ProgramBlackboardInputProjection.Direct && kind != TargetInputKind)
                throw new InvalidDataException("Blackboard target projection requires ActionTargetSnapshot input.");
            return projection switch
            {
                ProgramBlackboardInputProjection.Direct => kind switch
                {
                    ProgramInputValueKind.Boolean => ProgramStateValueKind.Boolean,
                    ProgramInputValueKind.Scalar => ProgramStateValueKind.Scalar,
                    ProgramInputValueKind.Vector2 => ProgramStateValueKind.Vector2,
                    ProgramInputValueKind.Vector3 => ProgramStateValueKind.Vector3,
                    _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
                },
                ProgramBlackboardInputProjection.ActionTargetIdentity => ProgramStateValueKind.Identity,
                ProgramBlackboardInputProjection.ActionTargetPosition => ProgramStateValueKind.Vector3,
                ProgramBlackboardInputProjection.ActionTargetYaw => ProgramStateValueKind.Scalar,
                _ => throw new ArgumentOutOfRangeException(nameof(projection), projection, null)
            };
        }
    }

}
