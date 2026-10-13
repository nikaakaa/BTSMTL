namespace ThirdPersonSimulation
{
    public interface IScalarMath<TScalar> where TScalar : struct
    {
        TScalar Zero { get; }
        TScalar One { get; }
        TScalar Add(TScalar left, TScalar right);
        TScalar Subtract(TScalar left, TScalar right);
        TScalar Multiply(TScalar left, TScalar right);
        TScalar Divide(TScalar left, TScalar right);
        TScalar Clamp(TScalar value, TScalar minimum, TScalar maximum);
        TScalar Negate(TScalar value);
        TScalar Max(TScalar left, TScalar right);
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
        TVector3 AddVector3(TVector3 left, TVector3 right);
        TVector3 SubtractVector3(TVector3 left, TVector3 right);
        TVector3 MultiplyVector3(TVector3 value, TScalar scale);
        TVector3 DivideVector3(TVector3 value, TScalar divisor);
        TVector3 AddVertical(TVector3 value, TScalar displacement);
        bool IsVector3Zero(TVector3 value);
    }

    public interface IPlanarRotation<TVector3, TYaw>
        where TVector3 : struct
        where TYaw : struct
    {
        TVector3 RotatePlanar(TVector3 value, TYaw yaw);
    }
}
