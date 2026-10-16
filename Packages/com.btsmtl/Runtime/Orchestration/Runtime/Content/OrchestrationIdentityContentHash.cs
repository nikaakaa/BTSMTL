using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    internal static class OrchestrationIdentityContentHash
    {
        internal static StableHash Table(ReadOnlySpan<string> entries)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Text(hash, "btsmtl-orchestration-identity-table/1");
            Integer(hash, entries.Length);
            for (int i = 0; i < entries.Length; i++)
                Text(hash, entries[i]);
            return Complete(hash);
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
            return Complete(hash);
        }

        internal static StableHash Actor<TValue>(StableHash identities, StableHash defaults,
            int defaultIdentityTableIndex,
            ReadOnlySpan<OrchestrationActorProgramSource<TValue>> sources,
            ReadOnlySpan<int> dependencyIdentityTableIndices,
            ReadOnlySpan<OrchestrationGraphBlackboardBinding> characterBlackboards,
            ReadOnlySpan<OrchestrationBlackboardSlot> descriptors,
            ReadOnlySpan<ReadOnlyMemory<int>> characterMappings)
            where TValue : unmanaged
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Text(hash, "btsmtl-orchestration-actor-preparation/1");
            Text(hash, identities.ToString());
            Text(hash, defaults.ToString());
            Integer(hash, defaultIdentityTableIndex);
            Integer(hash, dependencyIdentityTableIndices.Length);
            for (int dependencyIndex = 0; dependencyIndex < dependencyIdentityTableIndices.Length; dependencyIndex++)
                Integer(hash, dependencyIdentityTableIndices[dependencyIndex]);
            Integer(hash, sources.Length);
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                Text(hash, sources[programIndex].ContentIdentity.ToString());
                Integer(hash, sources[programIndex].IdentityTableIndex);
                ReadOnlySpan<int> mapping = characterMappings[programIndex].Span;
                Integer(hash, mapping.Length);
                for (int valueSlot = 0; valueSlot < mapping.Length; valueSlot++)
                    Integer(hash, mapping[valueSlot]);
            }
            Integer(hash, characterBlackboards.Length);
            for (int sharedSlot = 0; sharedSlot < characterBlackboards.Length; sharedSlot++)
            {
                ref readonly OrchestrationGraphBlackboardBinding binding = ref characterBlackboards[sharedSlot];
                ref readonly OrchestrationBlackboardSlot descriptor = ref descriptors[sharedSlot];
                Integer(hash, binding.InstanceIdentityId);
                Integer(hash, binding.ContractIdentityId);
                Integer(hash, binding.Field);
                Integer(hash, (int)descriptor.ScopeKind);
                Integer(hash, (int)descriptor.Lifetime);
                Integer(hash, descriptor.CompiledOwnerIndex);
                Integer(hash, (int)descriptor.ValueKind);
            }
            return Complete(hash);
        }

        private static StableHash Complete(IncrementalHash hash) => new StableHash(
            BitConverter.ToString(hash.GetHashAndReset()).Replace("-", string.Empty).ToLowerInvariant());

        private static void Text(IncrementalHash hash, string value)
        {
            Integer(hash, value.Length);
            ReadOnlySpan<char> characters = value.AsSpan();
            Span<byte> bytes = stackalloc byte[128];
            while (!characters.IsEmpty)
            {
                int count = Math.Min(bytes.Length / sizeof(char), characters.Length);
                for (int index = 0; index < count; index++)
                    BinaryPrimitives.WriteUInt16LittleEndian(bytes.Slice(index * sizeof(char), sizeof(char)), characters[index]);
                hash.AppendData(bytes.Slice(0, count * sizeof(char)));
                characters = characters.Slice(count);
            }
        }

        private static void Integer(IncrementalHash hash, int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            hash.AppendData(bytes);
        }
    }
}
