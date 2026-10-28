using System;
using System.Security.Cryptography;

namespace BTSMTL.Authoring.Storage
{
    public static class AuthoringFileRevision
    {
        public static string Compute(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty);
        }
    }
}
