using ThirdPersonSimulation;
using System;
using System.IO;

namespace ThirdPersonSimulation.Fixed
{
    [CanonicalContract]
    [CanonicalUnion(typeof(ProgramStateValue<FixedScalar, FixedVector2, FixedVector3, FixedYaw, CanonicalUnavailable, CanonicalUnavailable, SimulationActionTargetSnapshot>))]
    public readonly partial struct AbilityStateValue : IEquatable<AbilityStateValue>
    {
        public void Retain() => CanonicalRetain(in this);
        public void Release() => CanonicalRelease(in this);
        public static AbilityStateValue Default(ProgramStateValueKind kind) => CanonicalDefault(kind);

        public bool Equals(AbilityStateValue other) => Kind == other.Kind && (Kind switch
        {
            ProgramStateValueKind.Boolean => Boolean.Equals(other.Boolean),
            ProgramStateValueKind.Int32 => Int32.Equals(other.Int32),
            ProgramStateValueKind.UInt64 => UInt64.Equals(other.UInt64),
            ProgramStateValueKind.Scalar => Scalar.Equals(other.Scalar),
            ProgramStateValueKind.Vector2 => Vector2.Equals(other.Vector2),
            ProgramStateValueKind.Vector3 => Vector3.Equals(other.Vector3),
            ProgramStateValueKind.Yaw => Yaw.Equals(other.Yaw),
            ProgramStateValueKind.Identity => Identity.Equals(other.Identity),
            ProgramStateValueKind.BlackboardOwnerToken => BlackboardOwnerToken.Equals(other.BlackboardOwnerToken),
            ProgramStateValueKind.BlackboardWriteStamp => BlackboardWriteStamp.Equals(other.BlackboardWriteStamp),
            ProgramStateValueKind.ActionTargetSnapshot => ActionTargetSnapshot.Equals(other.ActionTargetSnapshot),
            (ProgramStateValueKind)0 => true,
            _ => throw new ArgumentOutOfRangeException(nameof(Kind))
        });

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
                ProgramStateValueKind.ActionTargetSnapshot when constant.Kind == ProgramConstantKind.Bytes =>
                    FromActionTargetSnapshot(SimulationActionTargetSnapshotCodec.Read(constant.Bytes.ToArray())),
                _ => throw new InvalidDataException(
                    $"Constant '{constant.Identity}' kind '{constant.Kind}' does not match state kind '{expectedKind}'.")
            };
            return value;
        }
    }
}
