namespace BTSMTL.Orchestration
{
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
        QuaternionAngleAxis = 14
    }

    public interface IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TQuaternion>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TQuaternion : unmanaged
    {
        OrchestrationValueKind Kind(in TValue value);
        bool Boolean(in TValue value);
        int Int32(in TValue value);
        ulong UInt64(in TValue value);
        TScalar Scalar(in TValue value);
        TVector2 Vector2(in TValue value);
        TVector3 Vector3(in TValue value);
        TQuaternion Quaternion(in TValue value);
        OrchestrationIdentityValue Identity(in TValue value);
        TValue FromBoolean(bool value);
        TValue FromInt32(int value);
        TValue FromUInt64(ulong value);
        TValue FromScalar(TScalar value);
        TValue FromVector2(TVector2 value);
        TValue FromVector3(TVector3 value);
        TValue FromQuaternion(TQuaternion value);
        TValue FromIdentity(OrchestrationIdentityValue value);
    }
}
