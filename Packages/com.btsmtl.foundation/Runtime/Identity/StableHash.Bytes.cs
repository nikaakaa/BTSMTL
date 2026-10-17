using System;
using System.Security.Cryptography;

namespace ThirdPersonSimulation
{
    public readonly partial struct StableHash
    {
        public static StableHash Compute(ReadOnlySpan<byte> payload)
        {
            using SHA256 algorithm = SHA256.Create();
            Span<byte> digest = stackalloc byte[32];
            algorithm.TryComputeHash(payload, digest, out _);
            return new StableHash(digest);
        }
    }
}
