using Photon.Deterministic;

namespace ThirdPersonSimulation.Configuration
{
    public static class BlackboardDefaultValues
    {
        public static FP FPGameplayNumber(long raw) => FP.FromRaw(raw);
        public static FPVector2 FPGameplayVector2(long x, long y) =>
            new FPVector2(FPGameplayNumber(x), FPGameplayNumber(y));
        public static FPVector3 FPGameplayVector3(long x, long y, long z) =>
            new FPVector3(FPGameplayNumber(x), FPGameplayNumber(y), FPGameplayNumber(z));
        public static Float32Scalar Float32Number(int bits) => Float32Scalar.FromBits(unchecked((uint)bits));
        public static Float32Vector2 Float32Vector2(int x, int y) =>
            new Float32Vector2(Float32Number(x), Float32Number(y));
        public static Float32Vector3 Float32Vector3(int x, int y, int z) =>
            new Float32Vector3(Float32Number(x), Float32Number(y), Float32Number(z));
        public static ulong UInt64(long bits) => unchecked((ulong)bits);
        public static CanonicalTextValue Text(bool isNull, int[] codeUnits)
        {
            if (isNull)
                return default;
            var characters = new char[codeUnits.Length];
            for (int index = 0; index < characters.Length; index++)
                characters[index] = (char)codeUnits[index];
            return CanonicalTextValue.FromString(new string(characters));
        }
    }
}
