using ThirdPersonSimulation;
namespace ThirdPersonSimulation.Fixed
{
    public static class FixedCanonicalData
    {
        public static void WriteScalar(this CanonicalWriter writer, FixedScalar value) => FixedScalar.CanonicalWrite(writer, in value);
        public static void WriteVector2(this CanonicalWriter writer, FixedVector2 value) => FixedVector2.CanonicalWrite(writer, in value);
        public static void WriteVector3(this CanonicalWriter writer, FixedVector3 value) => FixedVector3.CanonicalWrite(writer, in value);
        public static void WriteYaw(this CanonicalWriter writer, FixedYaw value) => FixedYaw.CanonicalWrite(writer, in value);
        public static FixedScalar ReadScalar(this CanonicalReader reader) => FixedScalar.CanonicalRead(reader);
        public static FixedVector2 ReadVector2(this CanonicalReader reader) => FixedVector2.CanonicalRead(reader);
        public static FixedVector3 ReadVector3(this CanonicalReader reader) => FixedVector3.CanonicalRead(reader);
        public static FixedYaw ReadYaw(this CanonicalReader reader) => FixedYaw.CanonicalRead(reader);
    }
}

