using System;
using System.Security.Cryptography;

namespace BTSMTL.Authoring.Workspace
{
    static class AuthoringFileRevision
    {
        internal static string Compute(byte[] bytes)
        {
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", string.Empty);
        }
    }
}
