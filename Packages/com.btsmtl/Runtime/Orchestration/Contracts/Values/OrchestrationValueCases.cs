using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    [ValueUnionTemplate]
    public readonly struct OrchestrationValueCases<TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
    {
        public OrchestrationValueKind Kind { get; }
        [ValueUnionCase] public bool Boolean { get; }
        [ValueUnionCase] public int Int32 { get; }
        [ValueUnionCase] public ulong UInt64 { get; }
        [ValueUnionCase] public TScalar Scalar { get; }
        [ValueUnionCase] public TVector2 Vector2 { get; }
        [ValueUnionCase] public TVector3 Vector3 { get; }
        [ValueUnionCase] public TYaw Yaw { get; }
        [ValueUnionCase] public OrchestrationIdentityValue Identity { get; }
        [ValueUnionCase] public TQuaternion Quaternion { get; }
        [ValueUnionCase] public OrchestrationEnumValue Enum { get; }
        [ValueUnionCase] public OrchestrationSpatialIdentity<TVector3, TYaw> SpatialIdentity { get; }
    }
}
