namespace ThirdPersonSimulation.Fixed
{
    public readonly struct FixedSimulationMath :
        IScalarMath<FixedScalar>,
        IVectorMath<FixedScalar, FixedVector2, FixedVector3>,
        IPlanarRotation<FixedVector3, FixedYaw>
    {
        public FixedScalar Zero => FixedScalar.Zero;
        public FixedScalar One => FixedScalar.One;
        public FixedVector2 ZeroVector2 => FixedVector2.Zero;
        public FixedVector3 ZeroVector3 => FixedVector3.Zero;

        public FixedScalar Add(FixedScalar left, FixedScalar right) => left + right;
        public FixedScalar Subtract(FixedScalar left, FixedScalar right) => left - right;
        public FixedScalar Multiply(FixedScalar left, FixedScalar right) => left * right;
        public FixedScalar Divide(FixedScalar left, FixedScalar right) => left / right;
        public FixedScalar Clamp(FixedScalar value, FixedScalar minimum, FixedScalar maximum) =>
            FixedScalar.Clamp(value, minimum, maximum);
        public bool GreaterThan(FixedScalar left, FixedScalar right) => left > right;
        public bool IsZero(FixedScalar value) => value == FixedScalar.Zero;

        public FixedVector3 AddVector3(FixedVector3 left, FixedVector3 right) => left + right;
        public FixedVector3 SubtractVector3(FixedVector3 left, FixedVector3 right) => left - right;
        public FixedVector3 MultiplyVector3(FixedVector3 value, FixedScalar scale) => value * scale;
        public FixedVector3 DivideVector3(FixedVector3 value, FixedScalar divisor) =>
            new FixedVector3(value.X / divisor, value.Y / divisor, value.Z / divisor);
        public bool IsVector3Zero(FixedVector3 value) => value == FixedVector3.Zero;
        public FixedVector3 RotatePlanar(FixedVector3 value, FixedYaw yaw) =>
            FixedAngle.RotatePlanar(value, yaw);
    }
}
