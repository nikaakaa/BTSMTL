using System;

namespace ThirdPersonSimulation
{
    public sealed class CanonicalInitializationTextResolver : ICanonicalTextResolver
    {
        public static readonly CanonicalInitializationTextResolver Instance = new CanonicalInitializationTextResolver();

        public CanonicalTextValue ResolveText(ArraySegment<byte> utf16) =>
            CanonicalTextValue.FromString(CanonicalTextEncoding.DecodeString(utf16));
    }
}
