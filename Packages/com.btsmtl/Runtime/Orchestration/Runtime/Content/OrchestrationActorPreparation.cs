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
            OrchestrationProgramSource<TValue>[] sources,
            ReadOnlyMemory<PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>> graphs,
            ReadOnlyMemory<OrchestrationProgram<TValue>> programs,
            ReadOnlyMemory<int>[] characterValueSlots,
            int[] dependencyIdentityTableIndices,
            OrchestrationGraphBlackboardBinding[] characterBlackboards,
            TValue[] characterValueDefaults,
            OrchestrationBlackboardSlot[] characterValueDescriptors,
            OrchestrationValueSlotType[] characterValueTypes,
            int[] characterValueKindSlotCounts,
            ReadOnlyMemory<OrchestrationPreparedProgramStorage<TValue>> preparedProgramStorages, StableHash identity)
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
        public ReadOnlyMemory<OrchestrationProgramSource<TValue>> Sources { get; }
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
            ReadOnlySpan<OrchestrationProgramSource<TValue>> sources,
            ReadOnlySpan<int> skillContentIds,
            ReadOnlySpan<int> dependencyIdentityTableIndices,
            OrchestrationBlackboardDefaults<TValue> defaults,
            int defaultIdentityTableIndex)
        {
            var prepared = OrchestrationGraphPreparation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>.Prepare(
                identities, sources, skillContentIds, (defaults, defaultIdentityTableIndex));

            var characters = new SortedDictionary<(int Instance, int Contract, int Field),
                (OrchestrationBlackboardSlot Descriptor, OrchestrationValueKind Kind, TValue DefaultValue)>();
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
                        characters.Add(key, (descriptor, kind,
                            prepared.Graphs.Span[programIndex].Program.DefaultValues.Span[binding.ValueSlot]));
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
                characterDefaults[sharedSlot] = entry.Value.DefaultValue;
                OrchestrationValueKind kind = entry.Value.Kind;
                characterSlotTypes[sharedSlot] = new OrchestrationValueSlotType(kind, characterKindSlotCounts[(int)kind]++);
                sharedSlot++;
            }

            var characterMappings = new ReadOnlyMemory<int>[sources.Length];
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                OrchestrationGraphContent<TValue> content = sources[programIndex].Content;
                ReadOnlySpan<int> mapping = identities.LocalToShared(sources[programIndex].IdentityTableIndex).Span;
                var characterMapping = new int[content.Program.DefaultValues.Length];
                Array.Fill(characterMapping, -1);
                foreach (ref readonly OrchestrationGraphBlackboardBinding binding in content.GeneratedBlackboards.Span)
                {
                    OrchestrationBlackboardSlot descriptor = content.Program.BlackboardSlots.Span[binding.ValueSlot];
                    if (descriptor.ScopeKind == OrchestrationScopeKind.Character)
                    {
                        int shared = sharedSlots[(mapping[binding.InstanceIdentityId], mapping[binding.ContractIdentityId], binding.Field)];
                        characterMapping[binding.ValueSlot] = shared;
                    }
                }
                for (int valueSlot = 0; valueSlot < characterMapping.Length; valueSlot++)
                    if (content.Program.BlackboardSlots.Span[valueSlot].ScopeKind == OrchestrationScopeKind.Character &&
                        characterMapping[valueSlot] < 0)
                        throw new InvalidOperationException($"图 '{content.Identity.GraphId}' 的 Character 黑板槽 '{valueSlot}' 缺少共享字段绑定。");
                characterMappings[programIndex] = characterMapping;
            }

            StableHash identity = OrchestrationIdentityContentHash.Actor(identities.Identity, defaults.ContentIdentity,
                defaultIdentityTableIndex, sources, dependencyIdentityTableIndices,
                characterBlackboards, characterDescriptors, characterSlotTypes, characterKindSlotCounts, characterMappings);
            return new OrchestrationActorPreparation<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>(
                identities, sources.ToArray(), prepared.Graphs, prepared.Programs, characterMappings,
                dependencyIdentityTableIndices.ToArray(), characterBlackboards,
                characterDefaults, characterDescriptors, characterSlotTypes, characterKindSlotCounts,
                prepared.PreparedProgramStorages, identity);
        }

    }
}
