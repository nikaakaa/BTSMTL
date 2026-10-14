namespace ThirdPersonSimulation
{
    public readonly partial struct Float32Quaternion : System.IEquatable<Float32Quaternion>
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
        public bool Equals(Float32Quaternion other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
        public static Float32Quaternion Identity => new(Float32Scalar.Zero, Float32Scalar.Zero,
            Float32Scalar.Zero, Float32Scalar.One);
    }
}
