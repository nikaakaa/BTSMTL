using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationActorPreparation<TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
    {
        readonly ReadOnlyMemory<int>[] m_CharacterValueSlots;

        OrchestrationActorPreparation(FrozenOrchestrationIdentities identities,
            OrchestrationActorProgramSource<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>[] sources,
            PreparedOrchestrationGraph<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] graphs,
            OrchestrationProgram<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>[] programs,
            ReadOnlyMemory<int>[] characterValueSlots,
            OrchestrationGraphBlackboardBinding[] characterBlackboards,
            OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] characterValueDefaults,
            OrchestrationBlackboardSlot[] characterValueDescriptors, StableHash identity)
        {
            Identities = identities;
            Sources = sources;
            Graphs = graphs;
            Programs = programs;
            m_CharacterValueSlots = characterValueSlots;
            CharacterBlackboards = characterBlackboards;
            CharacterValueDefaults = characterValueDefaults;
            CharacterValueDescriptors = characterValueDescriptors;
            Identity = identity;
        }

        public FrozenOrchestrationIdentities Identities { get; }
        public StableHash Identity { get; }
        public int ProgramCount => Programs.Length;
        public ReadOnlyMemory<OrchestrationActorProgramSource<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>> Sources { get; }
        public ReadOnlyMemory<PreparedOrchestrationGraph<TScalar, TVector2, TVector3, TYaw, TQuaternion>> Graphs { get; }
        public ReadOnlyMemory<OrchestrationProgram<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>> Programs { get; }
        public ReadOnlyMemory<OrchestrationGraphBlackboardBinding> CharacterBlackboards { get; }
        public ReadOnlyMemory<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> CharacterValueDefaults { get; }
        public ReadOnlyMemory<OrchestrationBlackboardSlot> CharacterValueDescriptors { get; }

        public ReadOnlyMemory<int> CharacterValueSlots(int programIndex) =>
            m_CharacterValueSlots[programIndex];

        public static OrchestrationActorPreparation<TScalar, TVector2, TVector3, TYaw, TQuaternion> Prepare(
            ReadOnlySpan<OrchestrationActorProgramSource<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>> sources,
            OrchestrationBlackboardDefaults<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> defaults,
            ReadOnlySpan<string> rosterTexts)
        {
            var tables = new OrchestrationIdentityTable[sources.Length + 1];
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
                tables[programIndex] = sources[programIndex].Content.Identities;
            tables[sources.Length] = defaults.Identities;
            FrozenOrchestrationIdentities identities = FrozenOrchestrationIdentities.Prepare(tables, rosterTexts);
            ReadOnlyMemory<int> defaultMapping = identities.LocalToShared(sources.Length);
            var projected = new Dictionary<(int Contract, int Field), OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>();
            foreach (ref readonly OrchestrationBlackboardDefault<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> value in defaults.Values.Span)
            {
                var sourceValue = value.Value;
                projected.Add((defaultMapping.Span[value.ContractIdentityId], value.Field),
                    PreparedOrchestrationGraph<TScalar, TVector2, TVector3, TYaw, TQuaternion>.MapValue(in sourceValue, defaultMapping));
            }

            var characters = new SortedDictionary<(int Instance, int Contract, int Field), OrchestrationBlackboardSlot>();
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                OrchestrationGraphContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> content = sources[programIndex].Content;
                ReadOnlySpan<int> mapping = identities.LocalToShared(programIndex).Span;
                foreach (ref readonly OrchestrationGraphBlackboardBinding binding in content.GeneratedBlackboards.Span)
                {
                    OrchestrationBlackboardSlot descriptor = content.Program.BlackboardSlots.Span[binding.ValueSlot];
                    if (descriptor.ScopeKind != OrchestrationScopeKind.Character)
                        continue;
                    var key = (Instance: mapping[binding.InstanceIdentityId], Contract: mapping[binding.ContractIdentityId], binding.Field);
                    if (characters.TryGetValue(key, out OrchestrationBlackboardSlot existing))
                    {
                        if (existing.ValueKind != descriptor.ValueKind || existing.Lifetime != descriptor.Lifetime ||
                            existing.CompiledOwnerIndex != descriptor.CompiledOwnerIndex)
                            throw new InvalidOperationException($"Character 黑板字段 '{identities.Table.Resolve(key.Instance)}/{identities.Table.Resolve(key.Contract)}/{key.Field}' 的发布声明不一致。");
                    }
                    else
                        characters.Add(key, descriptor);
                }
            }

            var sharedSlots = new Dictionary<(int Instance, int Contract, int Field), int>();
            var characterBlackboards = new OrchestrationGraphBlackboardBinding[characters.Count];
            var characterDefaults = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[characters.Count];
            var characterDescriptors = new OrchestrationBlackboardSlot[characters.Count];
            int sharedSlot = 0;
            foreach (KeyValuePair<(int Instance, int Contract, int Field), OrchestrationBlackboardSlot> entry in characters)
            {
                sharedSlots.Add(entry.Key, sharedSlot);
                characterBlackboards[sharedSlot] = new OrchestrationGraphBlackboardBinding(sharedSlot,
                    entry.Key.Instance, entry.Key.Contract, entry.Key.Field);
                characterDescriptors[sharedSlot] = entry.Value;
                sharedSlot++;
            }

            var graphs = new PreparedOrchestrationGraph<TScalar, TVector2, TVector3, TYaw, TQuaternion>[sources.Length];
            var programs = new OrchestrationProgram<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>[sources.Length];
            var characterMappings = new ReadOnlyMemory<int>[sources.Length];
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                OrchestrationGraphContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> content = sources[programIndex].Content;
                ReadOnlySpan<int> mapping = identities.LocalToShared(programIndex).Span;
                var overrides = new OrchestrationDefaultValue<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>[content.GeneratedBlackboards.Length];
                var characterMapping = new int[content.Program.DefaultValues.Length];
                Array.Fill(characterMapping, -1);
                for (int bindingIndex = 0; bindingIndex < overrides.Length; bindingIndex++)
                {
                    ref readonly OrchestrationGraphBlackboardBinding binding = ref content.GeneratedBlackboards.Span[bindingIndex];
                    int contract = mapping[binding.ContractIdentityId];
                    var value = RequiredDefault(projected, identities, contract, binding.Field);
                    OrchestrationBlackboardSlot descriptor = content.Program.BlackboardSlots.Span[binding.ValueSlot];
                    if (value.Kind != descriptor.ValueKind)
                        throw new InvalidOperationException($"图 '{content.Identity.GraphId}' 的黑板槽 '{binding.ValueSlot}' 要求 '{descriptor.ValueKind}'，当前发布默认值为 '{value.Kind}'。");
                    overrides[bindingIndex] = new OrchestrationDefaultValue<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>(
                        binding.ValueSlot, value);
                    if (descriptor.ScopeKind == OrchestrationScopeKind.Character)
                        characterMapping[binding.ValueSlot] = sharedSlots[(mapping[binding.InstanceIdentityId], contract, binding.Field)];
                }
                for (int valueSlot = 0; valueSlot < characterMapping.Length; valueSlot++)
                    if (content.Program.BlackboardSlots.Span[valueSlot].ScopeKind == OrchestrationScopeKind.Character &&
                        characterMapping[valueSlot] < 0)
                        throw new InvalidOperationException($"图 '{content.Identity.GraphId}' 的 Character 黑板槽 '{valueSlot}' 缺少共享字段绑定。");
                graphs[programIndex] = PreparedOrchestrationGraph<TScalar, TVector2, TVector3, TYaw, TQuaternion>.Prepare(
                    content, identities, programIndex, overrides);
                programs[programIndex] = new OrchestrationProgram<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>(graphs[programIndex].Program);
                characterMappings[programIndex] = characterMapping;
            }
            for (int characterSlot = 0; characterSlot < characterBlackboards.Length; characterSlot++)
            {
                ref readonly OrchestrationGraphBlackboardBinding binding = ref characterBlackboards[characterSlot];
                characterDefaults[characterSlot] = projected[(binding.ContractIdentityId, binding.Field)];
            }

            StableHash identity = PreparedIdentity(identities.Identity, defaults.ContentIdentity,
                sources, characterBlackboards, characterDescriptors, characterMappings);
            return new OrchestrationActorPreparation<TScalar, TVector2, TVector3, TYaw, TQuaternion>(
                identities, sources.ToArray(), graphs, programs, characterMappings, characterBlackboards,
                characterDefaults, characterDescriptors, identity);
        }

        static OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> RequiredDefault(
            Dictionary<(int Contract, int Field), OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> defaults,
            FrozenOrchestrationIdentities identities, int contract, int field)
        {
            if (!defaults.TryGetValue((contract, field), out var value))
                throw new InvalidOperationException($"当前发布默认值缺少黑板合同 '{identities.Table.Resolve(contract)}' 的字段 '{field}'。");
            return value;
        }

        static StableHash PreparedIdentity(StableHash identities, StableHash defaults,
            ReadOnlySpan<OrchestrationActorProgramSource<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>> sources,
            ReadOnlySpan<OrchestrationGraphBlackboardBinding> characterBlackboards,
            ReadOnlySpan<OrchestrationBlackboardSlot> descriptors,
            ReadOnlySpan<ReadOnlyMemory<int>> characterMappings)
        {
            var hash = new SimulationCanonicalPayloadHash.Accumulator();
            hash.Begin();
            Text(hash, "btsmtl-orchestration-actor-preparation/1");
            Text(hash, identities.ToString());
            Text(hash, defaults.ToString());
            Integer(hash, sources.Length);
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                Text(hash, sources[programIndex].ContentIdentity.ToString());
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
            return hash.Complete();
        }

        static void Text(SimulationCanonicalPayloadHash.Accumulator hash, string value)
        {
            Integer(hash, value.Length);
            hash.AppendText(value.AsSpan());
        }

        static void Integer(SimulationCanonicalPayloadHash.Accumulator hash, int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            hash.Append(bytes);
        }
    }
}
