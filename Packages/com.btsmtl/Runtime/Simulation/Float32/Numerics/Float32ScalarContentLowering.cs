namespace ThirdPersonSimulation
{
    public readonly struct Float32ScalarContentLowering : IScalarContentLowering<Float32Scalar>
    {
        public Float32Scalar FromSingle(float value) => Float32Scalar.FromDouble(value);
    }
}
