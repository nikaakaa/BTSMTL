using System;
using System.Buffers.Binary;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    internal static class OrchestrationIdentityContentHash
    {
        internal static StableHash Table(ReadOnlySpan<string> entries)
        {
            var hash = new SimulationCanonicalPayloadHash.Accumulator();
            hash.Begin();
            Text(hash, "btsmtl-orchestration-identity-table/1");
            Integer(hash, entries.Length);
            for (int i = 0; i < entries.Length; i++)
                Text(hash, entries[i]);
            return hash.Complete();
        }

        internal static StableHash Prepared(StableHash tableIdentity,
            ReadOnlySpan<OrchestrationIdentityTable> contentTables, ReadOnlySpan<ReadOnlyMemory<int>> mappings,
            ReadOnlySpan<int> rosterOrdinals)
        {
            var hash = new SimulationCanonicalPayloadHash.Accumulator();
            hash.Begin();
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
            return hash.Complete();
        }

        private static void Text(SimulationCanonicalPayloadHash.Accumulator hash, string value)
        {
            Integer(hash, value.Length);
            hash.AppendText(value.AsSpan());
        }

        private static void Integer(SimulationCanonicalPayloadHash.Accumulator hash, int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            hash.Append(bytes);
        }
    }
}
