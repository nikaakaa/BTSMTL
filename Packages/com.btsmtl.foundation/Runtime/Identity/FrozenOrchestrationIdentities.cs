using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class FrozenOrchestrationIdentities
    {
        private readonly ReadOnlyMemory<int>[] m_LocalToShared;

        private FrozenOrchestrationIdentities(OrchestrationIdentityTable table,
            ReadOnlyMemory<int>[] localToShared, int[] contentRanks, int[] rosterRanks, StableHash identity)
        {
            Table = table;
            m_LocalToShared = localToShared;
            Ordinals = new OrchestrationIdentityOrdinalView(contentRanks, rosterRanks, new int[1]);
            Identity = identity;
        }

        public OrchestrationIdentityTable Table { get; }
        public OrchestrationIdentityOrdinalView Ordinals { get; }
        public StableHash Identity { get; }
        public int ContentTableCount => m_LocalToShared.Length;

        public ReadOnlyMemory<int> LocalToShared(int tableIndex) => m_LocalToShared[tableIndex];

        public OrchestrationIdentityValue Content(int tableIndex, int localIdentity) =>
            OrchestrationIdentityValue.Content(m_LocalToShared[tableIndex].Span[localIdentity]);

        public string Resolve(in OrchestrationIdentityValue value) => value.Space == OrchestrationIdentitySpace.Null
            ? null : Table.Resolve(Ordinals.Ordinal(in value));

        public static FrozenOrchestrationIdentities Prepare(
            ReadOnlySpan<OrchestrationIdentityTable> contentTables, ReadOnlySpan<string> rosterTexts)
        {
            var texts = new List<string>();
            for (int tableIndex = 0; tableIndex < contentTables.Length; tableIndex++)
            {
                ReadOnlySpan<string> entries = contentTables[tableIndex].Entries.Span;
                for (int i = 0; i < entries.Length; i++)
                    texts.Add(entries[i]);
            }
            for (int i = 0; i < rosterTexts.Length; i++)
                texts.Add(rosterTexts[i]);
            OrchestrationIdentityTable table = OrchestrationIdentityPublication.Create(texts);
            return Prepare(table, contentTables, rosterTexts);
        }

        public static FrozenOrchestrationIdentities Prepare(OrchestrationIdentityTable table,
            ReadOnlySpan<OrchestrationIdentityTable> contentTables, ReadOnlySpan<string> rosterTexts)
        {
            var mappings = new ReadOnlyMemory<int>[contentTables.Length];
            for (int tableIndex = 0; tableIndex < contentTables.Length; tableIndex++)
            {
                ReadOnlySpan<string> entries = contentTables[tableIndex].Entries.Span;
                var mapping = new int[entries.Length + 1];
                for (int i = 0; i < entries.Length; i++)
                    mapping[i + 1] = table.Bind(entries[i]);
                mappings[tableIndex] = mapping;
            }
            var contentRanks = new int[table.Count + 1];
            for (int i = 0; i < contentRanks.Length; i++)
                contentRanks[i] = i;
            var rosterOrdinals = new int[rosterTexts.Length];
            for (int i = 0; i < rosterTexts.Length; i++)
                rosterOrdinals[i] = table.Bind(rosterTexts[i]);
            StableHash identity = OrchestrationIdentityPublicationHash.Prepared(table.Identity, contentTables,
                mappings, rosterOrdinals);
            return new FrozenOrchestrationIdentities(table, mappings, contentRanks, rosterOrdinals, identity);
        }
    }
}
