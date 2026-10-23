namespace BTSMTL.Orchestration
{
    public readonly struct OrchestrationValueSlotType
    {
        public OrchestrationValueSlotType(OrchestrationValueKind valueKind, int typeIndex)
        {
            ValueKind = valueKind;
            TypeIndex = typeIndex;
        }

        public OrchestrationValueKind ValueKind { get; }
        public int TypeIndex { get; }
    }

    public enum OrchestrationComparisonKind : byte
    {
        Equal = 0,
        NotEqual = 1,
        LessThan = 2,
        LessThanOrEqual = 3,
        GreaterThanOrEqual = 4,
        GreaterThan = 5
    }

    public enum OrchestrationAssignmentKind : byte
    {
        Set = 0,
        Add = 1,
        Subtract = 2,
        Multiply = 3,
        Divide = 4
    }

    public enum OrchestrationCalculationKind : byte
    {
        ScalarMultiply = 1,
        ScalarDivide = 2,
        IdentityEqual = 3,
        ScalarAbs = 4,
        ScalarClamp = 5,
        ScalarMoveTowards = 6,
        Vector3Planar = 7,
        Vector3Y = 8,
        Vector2Magnitude = 9,
        Vector2Normalize = 10,
        Vector2Subtract = 11,
        Vector2SignedAngle = 12,
        QuaternionForward = 13,
        QuaternionAngleAxis = 14,
        Vector2Dot = 15,
        Vector2Cross = 16
    }

    public interface IOrchestrationScalarValueAccess<TValue, TScalar>
        where TValue : unmanaged
        where TScalar : unmanaged
    {
        OrchestrationValueKind Kind(in TValue value);
        bool Boolean(in TValue value);
        int Int32(in TValue value);
        ulong UInt64(in TValue value);
        TScalar Scalar(in TValue value);
        TValue FromInt32(int value);
        TValue FromUInt64(ulong value);
        TValue FromScalar(TScalar value);
    }

    public interface IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion> :
        IOrchestrationScalarValueAccess<TValue, TScalar>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
    {
        TValue Default(OrchestrationValueKind kind);
        TVector2 Vector2(in TValue value);
        TVector3 Vector3(in TValue value);
        TYaw Yaw(in TValue value);
        TQuaternion Quaternion(in TValue value);
        OrchestrationIdentityValue Identity(in TValue value);
        OrchestrationEnumValue Enum(in TValue value);
        OrchestrationSpatialIdentity<TVector3, TYaw> SpatialIdentity(in TValue value);
        TValue FromBoolean(bool value);
        TValue FromVector2(TVector2 value);
        TValue FromVector3(TVector3 value);
        TValue FromYaw(TYaw value);
        TValue FromQuaternion(TQuaternion value);
        TValue FromIdentity(OrchestrationIdentityValue value);
        TValue FromEnum(OrchestrationEnumValue value);
        TValue FromSpatialIdentity(OrchestrationSpatialIdentity<TVector3, TYaw> value);
    }
}
