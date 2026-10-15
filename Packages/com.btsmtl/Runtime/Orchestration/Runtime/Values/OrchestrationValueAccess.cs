namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationValueAccess<TScalar, TVector2, TVector3, TYaw, TQuaternion> :
        IOrchestrationValueAccess<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>,
            TScalar, TVector2, TVector3, TQuaternion>
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
    {
        public OrchestrationValueKind Kind(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.Kind;
        public bool Boolean(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.Boolean;
        public int Int32(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.Int32;
        public ulong UInt64(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.UInt64;
        public TScalar Scalar(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.Scalar;
        public TVector2 Vector2(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.Vector2;
        public TVector3 Vector3(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.Vector3;
        public TYaw Yaw(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.Yaw;
        public TQuaternion Quaternion(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.Quaternion;
        public OrchestrationIdentityValue Identity(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.Identity;
        public OrchestrationEnumValue Enum(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.Enum;
        public OrchestrationSpatialIdentity<TVector3, TYaw> SpatialIdentity(
            in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value) => value.SpatialIdentity;

        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromBoolean(bool value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromBoolean(value);
        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromInt32(int value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromInt32(value);
        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromUInt64(ulong value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromUInt64(value);
        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromScalar(TScalar value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromScalar(value);
        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromVector2(TVector2 value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromVector2(value);
        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromVector3(TVector3 value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromVector3(value);
        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromYaw(TYaw value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromYaw(value);
        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromQuaternion(TQuaternion value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromQuaternion(value);
        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromIdentity(OrchestrationIdentityValue value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromIdentity(value);
        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromEnum(OrchestrationEnumValue value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromEnum(value);
        public OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> FromSpatialIdentity(
            OrchestrationSpatialIdentity<TVector3, TYaw> value) =>
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromSpatialIdentity(value);
    }
}
