using Photon.Deterministic;

namespace ThirdPersonSimulation.QuantumNumerics
{
    public readonly struct QuantumSimulationMath :
        IScalarMath<FP>,
        IVectorMath<FP, FPVector2, FPVector3>,
        IPlanarRotation<FPVector3, FP>,
        IYawMath<FP, FP>
    {
        public FP Zero => FP._0;
        public FP One => FP._1;
        public FP FromInt32(int value) => (FP)value;
        public FP FromInt64(long value) => FP.FromRaw(unchecked(value << FP.Precision));
        public int FloorToInt32(FP value) => FPMath.FloorToInt(value);
        public FPVector2 ZeroVector2 => FPVector2.Zero;
        public FPVector3 ZeroVector3 => FPVector3.Zero;

        public FP Add(FP left, FP right) => left + right;
        public FP Subtract(FP left, FP right) => left - right;
        public FP Multiply(FP left, FP right) => left * right;
        public FP Divide(FP left, FP right) => left / right;
        public FP Clamp(FP value, FP minimum, FP maximum) => FPMath.Clamp(value, minimum, maximum);
        public FP Negate(FP value) => -value;
        public FP Max(FP left, FP right) => FPMath.Max(left, right);
        public FP Min(FP left, FP right) => FPMath.Min(left, right);
        public FP Abs(FP value) => FPMath.Abs(value);
        public bool Equal(FP left, FP right) => left == right;
        public bool LessThanOrEqual(FP left, FP right) => left <= right;
        public bool GreaterThan(FP left, FP right) => left > right;
        public bool IsZero(FP value) => value == FP._0;

        public FPVector2 Vector2(FP x, FP y) => new FPVector2(x, y);
        public FPVector3 Vector3(FP x, FP y, FP z) => new FPVector3(x, y, z);
        public FP X2(FPVector2 value) => value.X;
        public FP Y2(FPVector2 value) => value.Y;
        public FP X3(FPVector3 value) => value.X;
        public FP Y3(FPVector3 value) => value.Y;
        public FP Z3(FPVector3 value) => value.Z;
        public FP Magnitude2(FPVector2 value) => value.Magnitude;
        public FP SqrMagnitude2(FPVector2 value) => value.SqrMagnitude;
        public FPVector2 Normalize2(FPVector2 value) => value.Normalized;
        public FPVector3 AddVector3(FPVector3 left, FPVector3 right) => left + right;
        public FPVector3 SubtractVector3(FPVector3 left, FPVector3 right) => left - right;
        public FPVector3 MultiplyVector3(FPVector3 value, FP scale) => value * scale;
        public FPVector3 DivideVector3(FPVector3 value, FP divisor) =>
            new FPVector3(value.X / divisor, value.Y / divisor, value.Z / divisor);
        public FPVector3 AddVertical(FPVector3 value, FP displacement) =>
            new FPVector3(value.X, value.Y + displacement, value.Z);
        public bool IsVector3Zero(FPVector3 value) => value == FPVector3.Zero;
        public bool IsVector2Zero(FPVector2 value) => value == FPVector2.Zero;

        public FP Yaw(FP degrees)
        {
            FP normalized = degrees % (FP)360;
            if (normalized >= (FP)180)
                normalized -= (FP)360;
            if (normalized < (FP)(-180))
                normalized += (FP)360;
            return normalized;
        }

        public FP Degrees(FP yaw) => yaw;
        public FP DeltaYaw(FP from, FP to) => Yaw(to - from);
        public FP YawFromDirection(FP x, FP z) => Yaw(FPMath.Atan2(x, z) * FP.Rad2Deg);

        public FPVector3 RotatePlanar(FPVector3 value, FP yawDegrees)
        {
            FPMath.SinCos(yawDegrees * FP.Deg2Rad, out FP sine, out FP cosine);
            return new FPVector3(
                value.X * cosine + value.Z * sine,
                value.Y,
                -value.X * sine + value.Z * cosine);
        }
    }
}
