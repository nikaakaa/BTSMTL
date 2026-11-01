using System;
using System.Security.Cryptography;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    internal static class OrchestrationIdentityPublicationHash
    {
        internal static StableHash Table(ReadOnlySpan<string> entries)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            StableHash.AppendLengthPrefixedText(hash, "btsmtl-orchestration-identity-table/1");
            StableHash.AppendInt32(hash, entries.Length);
            for (int i = 0; i < entries.Length; i++)
                StableHash.AppendLengthPrefixedText(hash, entries[i]);
            return StableHash.Compute(hash);
        }

        internal static StableHash Prepared(StableHash tableIdentity,
            ReadOnlySpan<OrchestrationIdentityTable> contentTables, ReadOnlySpan<ReadOnlyMemory<int>> mappings,
            ReadOnlySpan<int> rosterOrdinals)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            StableHash.AppendLengthPrefixedText(hash, "btsmtl-orchestration-prepared-identities/1");
            StableHash.AppendLengthPrefixedText(hash, tableIdentity.ToString());
            StableHash.AppendInt32(hash, rosterOrdinals.Length);
            for (int i = 0; i < rosterOrdinals.Length; i++)
                StableHash.AppendInt32(hash, rosterOrdinals[i]);
            StableHash.AppendInt32(hash, contentTables.Length);
            for (int tableIndex = 0; tableIndex < contentTables.Length; tableIndex++)
            {
                StableHash.AppendLengthPrefixedText(hash, contentTables[tableIndex].Identity.ToString());
                ReadOnlySpan<int> mapping = mappings[tableIndex].Span;
                StableHash.AppendInt32(hash, mapping.Length);
                for (int i = 0; i < mapping.Length; i++)
                    StableHash.AppendInt32(hash, mapping[i]);
            }
            return StableHash.Compute(hash);
        }
    }
}
