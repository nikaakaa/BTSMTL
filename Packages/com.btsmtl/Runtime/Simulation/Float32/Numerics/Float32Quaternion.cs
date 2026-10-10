namespace ThirdPersonSimulation
{
    public readonly struct Float32Quaternion
    {
        public Float32Quaternion(Float32Scalar x, Float32Scalar y, Float32Scalar z, Float32Scalar w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        public Float32Scalar X { get; }
        public Float32Scalar Y { get; }
        public Float32Scalar Z { get; }
        public Float32Scalar W { get; }
        public static Float32Quaternion Identity => new(Float32Scalar.Zero, Float32Scalar.Zero,
            Float32Scalar.Zero, Float32Scalar.One);
    }
}
