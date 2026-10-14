namespace ThirdPersonSimulation
{
    public interface IScalarMath<TScalar> where TScalar : struct
    {
        TScalar Zero { get; }
        TScalar One { get; }
        TScalar FromInt32(int value);
        TScalar FromInt64(long value);
        int FloorToInt32(TScalar value);
        TScalar Add(TScalar left, TScalar right);
        TScalar Subtract(TScalar left, TScalar right);
        TScalar Multiply(TScalar left, TScalar right);
        TScalar Divide(TScalar left, TScalar right);
        TScalar Clamp(TScalar value, TScalar minimum, TScalar maximum);
        TScalar Negate(TScalar value);
        TScalar Max(TScalar left, TScalar right);
        TScalar Min(TScalar left, TScalar right);
        TScalar Abs(TScalar value);
        bool GreaterThan(TScalar left, TScalar right);
        bool IsZero(TScalar value);
    }

    public interface IVectorMath<TScalar, TVector2, TVector3>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
    {
        TVector2 ZeroVector2 { get; }
        TVector3 ZeroVector3 { get; }
        TVector2 Vector2(TScalar x, TScalar y);
        TVector3 Vector3(TScalar x, TScalar y, TScalar z);
        TScalar X2(TVector2 value);
        TScalar Y2(TVector2 value);
        TScalar X3(TVector3 value);
        TScalar Y3(TVector3 value);
        TScalar Z3(TVector3 value);
        TScalar Magnitude2(TVector2 value);
        TScalar SqrMagnitude2(TVector2 value);
        TVector2 Normalize2(TVector2 value);
        TVector3 AddVector3(TVector3 left, TVector3 right);
        TVector3 SubtractVector3(TVector3 left, TVector3 right);
        TVector3 MultiplyVector3(TVector3 value, TScalar scale);
        TVector3 DivideVector3(TVector3 value, TScalar divisor);
        TVector3 AddVertical(TVector3 value, TScalar displacement);
        bool IsVector3Zero(TVector3 value);
        bool IsVector2Zero(TVector2 value);
    }

    public interface IPlanarRotation<TVector3, TYaw>
        where TVector3 : struct
        where TYaw : struct
    {
        TVector3 RotatePlanar(TVector3 value, TYaw yaw);
    }

    public interface IYawMath<TScalar, TYaw>
        where TScalar : struct
        where TYaw : struct
    {
        TYaw Yaw(TScalar degrees);
        TScalar Degrees(TYaw yaw);
        TScalar DeltaYaw(TYaw from, TYaw to);
        TYaw YawFromDirection(TScalar x, TScalar z);
    }
}
