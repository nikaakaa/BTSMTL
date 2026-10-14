using System;
using System.IO;

namespace ThirdPersonSimulation
{
    [ValueUnion(typeof(ProgramStateValue<Float32Scalar, Float32Vector2, Float32Vector3, Float32Yaw, Float32Quaternion, ProgramEnumState, SimulationActionTargetSnapshot>))]
    public readonly partial struct AbilityStateValue : IEquatable<AbilityStateValue>
    {

        public static AbilityStateValue FromEnum(CanonicalTextValue typeIdentity, int value) =>
            FromEnum(new ProgramEnumState(typeIdentity, value));


        public static AbilityStateValue FromConstant(ProgramConstant constant, ProgramStateValueKind expectedKind)
        {
            if (constant == null)
                throw new ArgumentNullException(nameof(constant));
            AbilityStateValue value = expectedKind switch
            {
                ProgramStateValueKind.Boolean when constant.Kind == ProgramConstantKind.Boolean => FromBoolean(constant.Boolean),
                ProgramStateValueKind.Int32 when constant.Kind == ProgramConstantKind.Int32 => FromInt32(constant.Int32),
                ProgramStateValueKind.UInt64 when constant.Kind == ProgramConstantKind.UInt64 => FromUInt64(constant.UInt64),
                ProgramStateValueKind.Scalar when constant.Kind == ProgramConstantKind.Scalar => FromScalar(constant.Scalar),
                ProgramStateValueKind.Vector2 when constant.Kind == ProgramConstantKind.Vector2 => FromVector2(constant.Vector2),
                ProgramStateValueKind.Vector3 when constant.Kind == ProgramConstantKind.Vector3 => FromVector3(constant.Vector3),
                ProgramStateValueKind.Yaw when constant.Kind == ProgramConstantKind.Yaw => FromYaw(constant.Yaw),
                ProgramStateValueKind.Identity when constant.Kind == ProgramConstantKind.String => FromIdentity(constant.Text),
                ProgramStateValueKind.Quaternion when constant.Kind == ProgramConstantKind.Quaternion => FromQuaternion(constant.Quaternion),
                ProgramStateValueKind.Enum when constant.Kind == ProgramConstantKind.Enum => FromEnum(constant.Enum.TypeIdentity, constant.Enum.Value),
                _ => throw new InvalidDataException(
                    $"Constant '{constant.Identity}' kind '{constant.Kind}' does not match state kind '{expectedKind}'.")
            };
            return value;
        }
    }
}
