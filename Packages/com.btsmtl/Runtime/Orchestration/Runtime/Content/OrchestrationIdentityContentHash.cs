using System;
using System.Security.Cryptography;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    internal static class OrchestrationIdentityContentHash
    {
        internal static StableHash Actor<TValue>(StableHash identities,
            ReadOnlySpan<OrchestrationProgramSource<TValue>> sources,
            ReadOnlySpan<int> dependencyIdentityTableIndices,
            ReadOnlySpan<OrchestrationGraphBlackboardBinding> characterBlackboards,
            ReadOnlySpan<OrchestrationBlackboardSlot> descriptors,
            ReadOnlySpan<OrchestrationValueSlotType> characterSlotTypes,
            ReadOnlySpan<int> characterKindSlotCounts,
            ReadOnlySpan<ReadOnlyMemory<int>> characterMappings)
            where TValue : unmanaged
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            StableHash.AppendLengthPrefixedText(hash, "btsmtl-orchestration-actor-preparation/4");
            StableHash.AppendLengthPrefixedText(hash, identities.ToString());
            StableHash.AppendInt32(hash, dependencyIdentityTableIndices.Length);
            for (int dependencyIndex = 0; dependencyIndex < dependencyIdentityTableIndices.Length; dependencyIndex++)
                StableHash.AppendInt32(hash, dependencyIdentityTableIndices[dependencyIndex]);
            StableHash.AppendInt32(hash, sources.Length);
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                StableHash.AppendLengthPrefixedText(hash, sources[programIndex].ContentIdentity.ToString());
                StableHash.AppendInt32(hash, sources[programIndex].IdentityTableIndex);
                ReadOnlySpan<int> mapping = characterMappings[programIndex].Span;
                StableHash.AppendInt32(hash, mapping.Length);
                for (int valueSlot = 0; valueSlot < mapping.Length; valueSlot++)
                    StableHash.AppendInt32(hash, mapping[valueSlot]);
            }
            StableHash.AppendInt32(hash, characterBlackboards.Length);
            for (int sharedSlot = 0; sharedSlot < characterBlackboards.Length; sharedSlot++)
            {
                ref readonly OrchestrationGraphBlackboardBinding binding = ref characterBlackboards[sharedSlot];
                ref readonly OrchestrationBlackboardSlot descriptor = ref descriptors[sharedSlot];
                StableHash.AppendInt32(hash, binding.InstanceIdentityId);
                StableHash.AppendInt32(hash, binding.DeclarationIdentityId);
                StableHash.AppendInt32(hash, (int)descriptor.ScopeKind);
                StableHash.AppendInt32(hash, (int)descriptor.Lifetime);
                StableHash.AppendInt32(hash, descriptor.CompiledOwnerIndex);
                StableHash.AppendInt32(hash, (int)descriptor.ValueKind);
                StableHash.AppendInt32(hash, (int)characterSlotTypes[sharedSlot].ValueKind);
                StableHash.AppendInt32(hash, characterSlotTypes[sharedSlot].TypeIndex);
            }
            StableHash.AppendInt32(hash, characterKindSlotCounts.Length);
            for (int kind = 0; kind < characterKindSlotCounts.Length; kind++)
                StableHash.AppendInt32(hash, characterKindSlotCounts[kind]);
            return StableHash.Compute(hash);
        }
    }
}
