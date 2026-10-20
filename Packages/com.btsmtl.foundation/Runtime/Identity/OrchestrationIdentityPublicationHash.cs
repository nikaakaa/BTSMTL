using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    internal static class OrchestrationIdentityPublicationHash
    {
        internal static StableHash Table(ReadOnlySpan<string> entries)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Text(hash, "btsmtl-orchestration-identity-table/1");
            Integer(hash, entries.Length);
            for (int i = 0; i < entries.Length; i++)
                Text(hash, entries[i]);
            return StableHash.Compute(hash);
        }

        internal static StableHash Prepared(StableHash tableIdentity,
            ReadOnlySpan<OrchestrationIdentityTable> contentTables, ReadOnlySpan<ReadOnlyMemory<int>> mappings,
            ReadOnlySpan<int> rosterOrdinals)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Text(hash, "btsmtl-orchestration-prepared-identities/1");
            Text(hash, tableIdentity.ToString());
            Integer(hash, rosterOrdinals.Length);
            for (int i = 0; i < rosterOrdinals.Length; i++)
                Integer(hash, rosterOrdinals[i]);
            Integer(hash, contentTables.Length);
            for (int tableIndex = 0; tableIndex < contentTables.Length; tableIndex++)
            {
                Text(hash, contentTables[tableIndex].Identity.ToString());
                ReadOnlySpan<int> mapping = mappings[tableIndex].Span;
                Integer(hash, mapping.Length);
                for (int i = 0; i < mapping.Length; i++)
                    Integer(hash, mapping[i]);
            }
            return StableHash.Compute(hash);
        }

        static void Text(IncrementalHash hash, string value)
        {
            Integer(hash, value.Length);
            StableHash.AppendText(hash, value.AsSpan());
        }

        static void Integer(IncrementalHash hash, int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            hash.AppendData(bytes);
        }
    }
}
