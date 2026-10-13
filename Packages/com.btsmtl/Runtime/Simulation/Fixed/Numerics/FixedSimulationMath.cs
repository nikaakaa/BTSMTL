namespace ThirdPersonSimulation.Fixed
{
    public readonly struct FixedSimulationMath :
        IScalarMath<FixedScalar>,
        IVectorMath<FixedScalar, FixedVector2, FixedVector3>,
        IPlanarRotation<FixedVector3, FixedYaw>,
        IYawMath<FixedScalar, FixedYaw>
    {
        public FixedScalar Zero => FixedScalar.Zero;
        public FixedScalar One => FixedScalar.One;
        public FixedScalar FromInt32(int value) => FixedScalar.FromInt64(value);
        public int FloorToInt32(FixedScalar value) => checked((int)(value.Raw >> FixedScalar.FractionalBits));
        public FixedVector2 ZeroVector2 => FixedVector2.Zero;
        public FixedVector3 ZeroVector3 => FixedVector3.Zero;

        public FixedScalar Add(FixedScalar left, FixedScalar right) => left + right;
        public FixedScalar Subtract(FixedScalar left, FixedScalar right) => left - right;
        public FixedScalar Multiply(FixedScalar left, FixedScalar right) => left * right;
        public FixedScalar Divide(FixedScalar left, FixedScalar right) => left / right;
        public FixedScalar Clamp(FixedScalar value, FixedScalar minimum, FixedScalar maximum) =>
            FixedScalar.Clamp(value, minimum, maximum);
        public FixedScalar Negate(FixedScalar value) => -value;
        public FixedScalar Max(FixedScalar left, FixedScalar right) => FixedScalar.Max(left, right);
        public FixedScalar Min(FixedScalar left, FixedScalar right) => FixedScalar.Min(left, right);
        public FixedScalar Abs(FixedScalar value) => FixedScalar.Abs(value);
        public bool GreaterThan(FixedScalar left, FixedScalar right) => left > right;
        public bool IsZero(FixedScalar value) => value == FixedScalar.Zero;

        public FixedVector2 Vector2(FixedScalar x, FixedScalar y) => new FixedVector2(x, y);
        public FixedVector3 Vector3(FixedScalar x, FixedScalar y, FixedScalar z) => new FixedVector3(x, y, z);
        public FixedScalar X2(FixedVector2 value) => value.X;
        public FixedScalar Y2(FixedVector2 value) => value.Y;
        public FixedScalar X3(FixedVector3 value) => value.X;
        public FixedScalar Y3(FixedVector3 value) => value.Y;
        public FixedScalar Z3(FixedVector3 value) => value.Z;
        public FixedScalar Magnitude2(FixedVector2 value) => value.Magnitude;
        public FixedScalar SqrMagnitude2(FixedVector2 value) => value.SqrMagnitude;
        public FixedVector2 Normalize2(FixedVector2 value) => value.Normalized;
        public FixedVector3 AddVector3(FixedVector3 left, FixedVector3 right) => left + right;
        public FixedVector3 SubtractVector3(FixedVector3 left, FixedVector3 right) => left - right;
        public FixedVector3 MultiplyVector3(FixedVector3 value, FixedScalar scale) => value * scale;
        public FixedVector3 DivideVector3(FixedVector3 value, FixedScalar divisor) =>
            new FixedVector3(value.X / divisor, value.Y / divisor, value.Z / divisor);
        public FixedVector3 AddVertical(FixedVector3 value, FixedScalar displacement) =>
            new FixedVector3(value.X, value.Y + displacement, value.Z);
        public bool IsVector3Zero(FixedVector3 value) => value == FixedVector3.Zero;
        public bool IsVector2Zero(FixedVector2 value) => value == FixedVector2.Zero;
        public FixedYaw Yaw(FixedScalar degrees) => new FixedYaw(degrees);
        public FixedScalar Degrees(FixedYaw yaw) => yaw.Degrees;
        public FixedScalar DeltaYaw(FixedYaw from, FixedYaw to) => FixedAngle.Delta(from, to);
        public FixedYaw YawFromDirection(FixedScalar x, FixedScalar z) => FixedAngle.FromPlanarDirection(x, z);
        public FixedVector3 RotatePlanar(FixedVector3 value, FixedYaw yaw) =>
            FixedAngle.RotatePlanar(value, yaw);
    }
}
