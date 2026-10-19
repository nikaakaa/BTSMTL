using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationActorPreparation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
    {
        readonly ReadOnlyMemory<int>[] m_CharacterValueSlots;
        readonly ReadOnlyMemory<int> m_DependencyIdentityTableIndices;

        OrchestrationActorPreparation(FrozenOrchestrationIdentities identities,
            OrchestrationActorProgramSource<TValue>[] sources,
            PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>[] graphs,
            OrchestrationProgram<TValue>[] programs,
            ReadOnlyMemory<int>[] characterValueSlots,
            int[] dependencyIdentityTableIndices,
            OrchestrationGraphBlackboardBinding[] characterBlackboards,
            TValue[] characterValueDefaults,
            OrchestrationBlackboardSlot[] characterValueDescriptors,
            OrchestrationValueSlotType[] characterValueTypes,
            int[] characterValueKindSlotCounts,
            OrchestrationPreparedProgramStorage<TValue>[] preparedProgramStorages, StableHash identity)
        {
            Identities = identities;
            Sources = sources;
            Graphs = graphs;
            Programs = programs;
            m_CharacterValueSlots = characterValueSlots;
            m_DependencyIdentityTableIndices = dependencyIdentityTableIndices;
            CharacterBlackboards = characterBlackboards;
            CharacterValueDefaults = characterValueDefaults;
            CharacterValueDescriptors = characterValueDescriptors;
            CharacterValueTypes = characterValueTypes;
            CharacterValueKindSlotCounts = characterValueKindSlotCounts;
            PreparedProgramStorages = preparedProgramStorages;
            Identity = identity;
        }

        public FrozenOrchestrationIdentities Identities { get; }
        public StableHash Identity { get; }
        public int ProgramCount => Programs.Length;
        public int DependencyIdentityTableCount => m_DependencyIdentityTableIndices.Length;
        public ReadOnlyMemory<OrchestrationActorProgramSource<TValue>> Sources { get; }
        public ReadOnlyMemory<PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>> Graphs { get; }
        public ReadOnlyMemory<OrchestrationProgram<TValue>> Programs { get; }
        public ReadOnlyMemory<OrchestrationGraphBlackboardBinding> CharacterBlackboards { get; }
        public ReadOnlyMemory<TValue> CharacterValueDefaults { get; }
        public ReadOnlyMemory<OrchestrationBlackboardSlot> CharacterValueDescriptors { get; }
        public ReadOnlyMemory<OrchestrationValueSlotType> CharacterValueTypes { get; }
        public ReadOnlyMemory<int> CharacterValueKindSlotCounts { get; }
        public ReadOnlyMemory<OrchestrationPreparedProgramStorage<TValue>> PreparedProgramStorages { get; }

        public ReadOnlyMemory<int> CharacterValueSlots(int programIndex) =>
            m_CharacterValueSlots[programIndex];

        public ReadOnlyMemory<int> DependencyIdentityMapping(int dependencyIndex) =>
            Identities.LocalToShared(m_DependencyIdentityTableIndices.Span[dependencyIndex]);

        public OrchestrationExecutionCapacity PrepareExecutionCapacity(
            ReadOnlySpan<OrchestrationRootAdmission> admissions,
            ReadOnlySpan<OrchestrationTimelineCallCapacity> calls) =>
            OrchestrationExecutionFrames<TValue>.PrepareCapacity(Programs.Span, admissions, calls);

        public OrchestrationPreparedStateLayout<TValue>
            PrepareStateLayout<TPlayback>(OrchestrationExecutionCapacity executionCapacity,
                in OrchestrationTimelineStateLayout<TPlayback> timelineLayout,
                NumericProfileId numericProfile, TargetAbiVersion targetAbi,
                Func<StableHash, NumericProfileId, TargetAbiVersion, int, int,
                    OrchestrationCharacterBlackboardStateLayout<TValue>> blackboardFactory,
                Func<StableHash, NumericProfileId, TargetAbiVersion, int, int, int, int, int, int,
                    OrchestrationExecutionStateLayout<TValue>> executionFactory,
                Func<StableHash, NumericProfileId, TargetAbiVersion,
                    OrchestrationCharacterBlackboardStateLayout<TValue>,
                    OrchestrationExecutionStateLayout<TValue>,
                    OrchestrationTimelineStateLayout<TPlayback>,
                    OrchestrationStateLayout<TValue, TPlayback>> compositeFactory)
            where TPlayback : unmanaged
        {
            OrchestrationExecutionStateCapacity capacity = executionCapacity.State;
            int characterCount = CharacterValueDefaults.Length;
            var blackboard = blackboardFactory(Identity, numericProfile, targetAbi,
                characterCount, characterCount);
            var execution = executionFactory(Identity, numericProfile, targetAbi,
                capacity.ValueCount, capacity.FrameCount, capacity.OperationCount, 1,
                capacity.SavedOutputCount, capacity.ValueCount);
            var composite = compositeFactory(Identity, numericProfile, targetAbi,
                blackboard, execution, timelineLayout);
            return new OrchestrationPreparedStateLayout<TValue>(composite.Layout, composite.StateSegments,
                capacity.FrameCount, PreparedProgramStorages, CharacterValueTypes,
                composite.GetTimelineRegion(), timelineLayout.Layout);
        }

        public static OrchestrationActorPreparation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues> Prepare(
            FrozenOrchestrationIdentities identities,
            ReadOnlySpan<OrchestrationActorProgramSource<TValue>> sources,
            ReadOnlySpan<int> skillContentIds,
            ReadOnlySpan<int> dependencyIdentityTableIndices,
            OrchestrationBlackboardDefaults<TValue> defaults,
            int defaultIdentityTableIndex)
        {
            ReadOnlyMemory<int> defaultMapping = identities.LocalToShared(defaultIdentityTableIndex);
            var projected = new Dictionary<(int Contract, int Field), TValue>();
            foreach (ref readonly OrchestrationBlackboardDefault<TValue> value in defaults.Values.Span)
            {
                var sourceValue = value.Value;
                projected.Add((defaultMapping.Span[value.ContractIdentityId], value.Field),
                    PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>.MapValue(in sourceValue, defaultMapping));
            }

            var characters = new SortedDictionary<(int Instance, int Contract, int Field),
                (OrchestrationBlackboardSlot Descriptor, OrchestrationValueKind Kind)>();
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                OrchestrationGraphContent<TValue> content = sources[programIndex].Content;
                ReadOnlySpan<int> mapping = identities.LocalToShared(sources[programIndex].IdentityTableIndex).Span;
                foreach (ref readonly OrchestrationGraphBlackboardBinding binding in content.GeneratedBlackboards.Span)
                {
                    OrchestrationBlackboardSlot descriptor = content.Program.BlackboardSlots.Span[binding.ValueSlot];
                    if (descriptor.ScopeKind != OrchestrationScopeKind.Character)
                        continue;
                    OrchestrationValueKind kind = content.Program.ValueSlotTypes.Span[binding.ValueSlot].ValueKind;
                    var key = (Instance: mapping[binding.InstanceIdentityId], Contract: mapping[binding.ContractIdentityId], binding.Field);
                    if (characters.TryGetValue(key, out var existing))
                    {
                        if (existing.Kind != kind || existing.Descriptor.Lifetime != descriptor.Lifetime ||
                            existing.Descriptor.CompiledOwnerIndex != descriptor.CompiledOwnerIndex)
                            throw new InvalidOperationException($"Character 黑板字段 '{identities.Table.Resolve(key.Instance)}/{identities.Table.Resolve(key.Contract)}/{key.Field}' 的发布声明不一致。");
                    }
                    else
                        characters.Add(key, (descriptor, kind));
                }
            }

            var sharedSlots = new Dictionary<(int Instance, int Contract, int Field), int>();
            var characterBlackboards = new OrchestrationGraphBlackboardBinding[characters.Count];
            var characterDefaults = new TValue[characters.Count];
            var characterDescriptors = new OrchestrationBlackboardSlot[characters.Count];
            var characterSlotTypes = new OrchestrationValueSlotType[characters.Count];
            var characterKindSlotCounts = new int[(int)OrchestrationValueKind.SpatialIdentity + 1];
            int sharedSlot = 0;
            foreach (var entry in characters)
            {
                sharedSlots.Add(entry.Key, sharedSlot);
                characterBlackboards[sharedSlot] = new OrchestrationGraphBlackboardBinding(sharedSlot,
                    entry.Key.Instance, entry.Key.Contract, entry.Key.Field);
                characterDescriptors[sharedSlot] = entry.Value.Descriptor;
                OrchestrationValueKind kind = entry.Value.Kind;
                characterSlotTypes[sharedSlot] = new OrchestrationValueSlotType(kind, characterKindSlotCounts[(int)kind]++);
                sharedSlot++;
            }

            var graphs = new PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>[sources.Length];
            var programs = new OrchestrationProgram<TValue>[sources.Length];
            var preparedProgramStorages = new OrchestrationPreparedProgramStorage<TValue>[sources.Length];
            var characterMappings = new ReadOnlyMemory<int>[sources.Length];
            TValues values = default;
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                OrchestrationGraphContent<TValue> content = sources[programIndex].Content;
                ReadOnlySpan<int> mapping = identities.LocalToShared(sources[programIndex].IdentityTableIndex).Span;
                var overrides = new OrchestrationDefaultValue<TValue>[content.GeneratedBlackboards.Length];
                var characterMapping = new int[content.Program.DefaultValues.Length];
                Array.Fill(characterMapping, -1);
                for (int bindingIndex = 0; bindingIndex < overrides.Length; bindingIndex++)
                {
                    ref readonly OrchestrationGraphBlackboardBinding binding = ref content.GeneratedBlackboards.Span[bindingIndex];
                    int contract = mapping[binding.ContractIdentityId];
                    var value = RequiredDefault(projected, identities, contract, binding.Field);
                    OrchestrationBlackboardSlot descriptor = content.Program.BlackboardSlots.Span[binding.ValueSlot];
                    OrchestrationValueKind declaredKind = content.Program.ValueSlotTypes.Span[binding.ValueSlot].ValueKind;
                    OrchestrationValueKind kind = values.Kind(in value);
                    if (kind != declaredKind)
                        throw new InvalidOperationException($"图 '{content.Identity.GraphId}' 的黑板槽 '{binding.ValueSlot}' 要求 '{declaredKind}'，当前发布默认值为 '{kind}'。");
                    overrides[bindingIndex] = new OrchestrationDefaultValue<TValue>(
                        binding.ValueSlot, value);
                    if (descriptor.ScopeKind == OrchestrationScopeKind.Character)
                        characterMapping[binding.ValueSlot] = sharedSlots[(mapping[binding.InstanceIdentityId], contract, binding.Field)];
                }
                for (int valueSlot = 0; valueSlot < characterMapping.Length; valueSlot++)
                    if (content.Program.BlackboardSlots.Span[valueSlot].ScopeKind == OrchestrationScopeKind.Character &&
                        characterMapping[valueSlot] < 0)
                        throw new InvalidOperationException($"图 '{content.Identity.GraphId}' 的 Character 黑板槽 '{valueSlot}' 缺少共享字段绑定。");
                graphs[programIndex] = PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>.Prepare(
                    content, identities, sources[programIndex].IdentityTableIndex, overrides);
                programs[programIndex] = new OrchestrationProgram<TValue>(graphs[programIndex].Program);
                OrchestrationProgramContent<TValue> program = graphs[programIndex].Program;
                preparedProgramStorages[programIndex] = new OrchestrationPreparedProgramStorage<TValue>(
                    skillContentIds[programIndex], program.ExecutionStorage,
                    program.ValueSlotTypes);
                characterMappings[programIndex] = characterMapping;
            }
            for (int characterSlot = 0; characterSlot < characterBlackboards.Length; characterSlot++)
            {
                ref readonly OrchestrationGraphBlackboardBinding binding = ref characterBlackboards[characterSlot];
                characterDefaults[characterSlot] = projected[(binding.ContractIdentityId, binding.Field)];
            }

            StableHash identity = OrchestrationIdentityContentHash.Actor(identities.Identity, defaults.ContentIdentity,
                defaultIdentityTableIndex, sources, dependencyIdentityTableIndices,
                characterBlackboards, characterDescriptors, characterSlotTypes, characterKindSlotCounts, characterMappings);
            return new OrchestrationActorPreparation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>(
                identities, sources.ToArray(), graphs, programs, characterMappings,
                dependencyIdentityTableIndices.ToArray(), characterBlackboards,
                characterDefaults, characterDescriptors, characterSlotTypes, characterKindSlotCounts,
                preparedProgramStorages, identity);
        }

        static TValue RequiredDefault(
            Dictionary<(int Contract, int Field), TValue> defaults,
            FrozenOrchestrationIdentities identities, int contract, int field)
        {
            if (!defaults.TryGetValue((contract, field), out var value))
                throw new InvalidOperationException($"当前发布默认值缺少黑板合同 '{identities.Table.Resolve(contract)}' 的字段 '{field}'。");
            return value;
        }

    }
}
