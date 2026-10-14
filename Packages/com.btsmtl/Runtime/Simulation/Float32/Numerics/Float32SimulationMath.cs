namespace ThirdPersonSimulation
{
    public readonly struct Float32SimulationMath :
        IScalarMath<Float32Scalar>,
        IVectorMath<Float32Scalar, Float32Vector2, Float32Vector3>,
        IPlanarRotation<Float32Vector3, Float32Yaw>,
        IYawMath<Float32Scalar, Float32Yaw>
    {
        public Float32Scalar Zero => Float32Scalar.Zero;
        public Float32Scalar One => Float32Scalar.One;
        public Float32Scalar FromInt32(int value) => Float32Scalar.FromInt64(value);
        public Float32Scalar FromInt64(long value) => Float32Scalar.FromInt64(value);
        public int FloorToInt32(Float32Scalar value) => (int)System.Math.Floor(value.ToDouble());
        public Float32Vector2 ZeroVector2 => Float32Vector2.Zero;
        public Float32Vector3 ZeroVector3 => Float32Vector3.Zero;

        public Float32Scalar Add(Float32Scalar left, Float32Scalar right) => left + right;
        public Float32Scalar Subtract(Float32Scalar left, Float32Scalar right) => left - right;
        public Float32Scalar Multiply(Float32Scalar left, Float32Scalar right) => left * right;
        public Float32Scalar Divide(Float32Scalar left, Float32Scalar right) => left / right;
        public Float32Scalar Clamp(Float32Scalar value, Float32Scalar minimum, Float32Scalar maximum) =>
            Float32Scalar.Clamp(value, minimum, maximum);
        public Float32Scalar Negate(Float32Scalar value) => -value;
        public Float32Scalar Max(Float32Scalar left, Float32Scalar right) => Float32Scalar.Max(left, right);
        public Float32Scalar Min(Float32Scalar left, Float32Scalar right) => Float32Scalar.Min(left, right);
        public Float32Scalar Abs(Float32Scalar value) => Float32Scalar.Abs(value);
        public bool GreaterThan(Float32Scalar left, Float32Scalar right) => left > right;
        public bool IsZero(Float32Scalar value) => value == Float32Scalar.Zero;

        public Float32Vector2 Vector2(Float32Scalar x, Float32Scalar y) => new Float32Vector2(x, y);
        public Float32Vector3 Vector3(Float32Scalar x, Float32Scalar y, Float32Scalar z) => new Float32Vector3(x, y, z);
        public Float32Scalar X2(Float32Vector2 value) => value.X;
        public Float32Scalar Y2(Float32Vector2 value) => value.Y;
        public Float32Scalar X3(Float32Vector3 value) => value.X;
        public Float32Scalar Y3(Float32Vector3 value) => value.Y;
        public Float32Scalar Z3(Float32Vector3 value) => value.Z;
        public Float32Scalar Magnitude2(Float32Vector2 value) => value.Magnitude;
        public Float32Scalar SqrMagnitude2(Float32Vector2 value) => value.SqrMagnitude;
        public Float32Vector2 Normalize2(Float32Vector2 value) => value.Normalized;
        public Float32Vector3 AddVector3(Float32Vector3 left, Float32Vector3 right) => left + right;
        public Float32Vector3 SubtractVector3(Float32Vector3 left, Float32Vector3 right) => left - right;
        public Float32Vector3 MultiplyVector3(Float32Vector3 value, Float32Scalar scale) => value * scale;
        public Float32Vector3 DivideVector3(Float32Vector3 value, Float32Scalar divisor) =>
            new Float32Vector3(value.X / divisor, value.Y / divisor, value.Z / divisor);
        public Float32Vector3 AddVertical(Float32Vector3 value, Float32Scalar displacement) =>
            new Float32Vector3(value.X, value.Y + displacement, value.Z);
        public bool IsVector3Zero(Float32Vector3 value) => value == Float32Vector3.Zero;
        public bool IsVector2Zero(Float32Vector2 value) => value == Float32Vector2.Zero;
        public Float32Yaw Yaw(Float32Scalar degrees) => new Float32Yaw(degrees);
        public Float32Scalar Degrees(Float32Yaw yaw) => yaw.Degrees;
        public Float32Scalar DeltaYaw(Float32Yaw from, Float32Yaw to) => Float32Angle.Delta(from, to);
        public Float32Yaw YawFromDirection(Float32Scalar x, Float32Scalar z) => Float32Angle.FromPlanarDirection(x, z);
        public Float32Vector3 RotatePlanar(Float32Vector3 value, Float32Yaw yaw) =>
            Float32Angle.RotatePlanar(value, yaw);
    }
}
