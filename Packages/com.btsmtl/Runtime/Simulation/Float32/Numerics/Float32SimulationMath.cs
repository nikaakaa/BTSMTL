namespace ThirdPersonSimulation
{
    public readonly struct Float32SimulationMath :
        IScalarMath<Float32Scalar>,
        IVectorMath<Float32Scalar, Float32Vector2, Float32Vector3>,
        IPlanarRotation<Float32Vector3, Float32Yaw>
    {
        public Float32Scalar Zero => Float32Scalar.Zero;
        public Float32Scalar One => Float32Scalar.One;
        public Float32Vector2 ZeroVector2 => Float32Vector2.Zero;
        public Float32Vector3 ZeroVector3 => Float32Vector3.Zero;

        public Float32Scalar Add(Float32Scalar left, Float32Scalar right) => left + right;
        public Float32Scalar Subtract(Float32Scalar left, Float32Scalar right) => left - right;
        public Float32Scalar Multiply(Float32Scalar left, Float32Scalar right) => left * right;
        public Float32Scalar Divide(Float32Scalar left, Float32Scalar right) => left / right;
        public Float32Scalar Clamp(Float32Scalar value, Float32Scalar minimum, Float32Scalar maximum) =>
            Float32Scalar.Clamp(value, minimum, maximum);
        public bool GreaterThan(Float32Scalar left, Float32Scalar right) => left > right;
        public bool IsZero(Float32Scalar value) => value == Float32Scalar.Zero;

        public Float32Vector3 AddVector3(Float32Vector3 left, Float32Vector3 right) => left + right;
        public Float32Vector3 SubtractVector3(Float32Vector3 left, Float32Vector3 right) => left - right;
        public Float32Vector3 MultiplyVector3(Float32Vector3 value, Float32Scalar scale) => value * scale;
        public Float32Vector3 DivideVector3(Float32Vector3 value, Float32Scalar divisor) =>
            new Float32Vector3(value.X / divisor, value.Y / divisor, value.Z / divisor);
        public bool IsVector3Zero(Float32Vector3 value) => value == Float32Vector3.Zero;
        public Float32Vector3 RotatePlanar(Float32Vector3 value, Float32Yaw yaw) =>
            Float32Angle.RotatePlanar(value, yaw);
    }
}
