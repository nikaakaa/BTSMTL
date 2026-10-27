using System.Globalization;

namespace BTSMTL.Diagnostics
{
    public static class SourceContentHasher
    {
        public static string Hash(params string[] values)
        {
            ulong hash = 14695981039346656037UL;
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++)
                {
                    string value = values[i] ?? string.Empty;
                    for (int character = 0; character < value.Length; character++)
                    {
                        hash ^= value[character];
                        hash *= 1099511628211UL;
                    }
                    hash ^= 0xff;
                    hash *= 1099511628211UL;
                }
            }
            return hash.ToString("x16", CultureInfo.InvariantCulture);
        }
    }
}
