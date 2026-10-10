namespace ThirdPersonSimulation
{
    public static class Float32CanonicalData
    {
        public static void WriteScalar(this CanonicalWriter writer, Float32Scalar value) => Float32Scalar.CanonicalWrite(writer, in value);
        public static void WriteVector2(this CanonicalWriter writer, Float32Vector2 value) => Float32Vector2.CanonicalWrite(writer, in value);
        public static void WriteVector3(this CanonicalWriter writer, Float32Vector3 value) => Float32Vector3.CanonicalWrite(writer, in value);
        public static void WriteYaw(this CanonicalWriter writer, Float32Yaw value) => Float32Yaw.CanonicalWrite(writer, in value);
        public static Float32Scalar ReadScalar(this CanonicalReader reader) => Float32Scalar.CanonicalRead(reader);
        public static Float32Vector2 ReadVector2(this CanonicalReader reader) => Float32Vector2.CanonicalRead(reader);
        public static Float32Vector3 ReadVector3(this CanonicalReader reader) => Float32Vector3.CanonicalRead(reader);
        public static Float32Yaw ReadYaw(this CanonicalReader reader) => Float32Yaw.CanonicalRead(reader);
    }
}
