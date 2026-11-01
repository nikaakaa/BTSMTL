using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.Orchestration
{
    public sealed class OrchestrationActorPreparation<TValue, TNumerics>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
    {
        readonly ReadOnlyMemory<int>[] m_CharacterValueSlots;
        readonly ReadOnlyMemory<int> m_DependencyIdentityTableIndices;

        OrchestrationActorPreparation(FrozenOrchestrationIdentities identities,
            OrchestrationProgramSource<TValue>[] sources,
            ReadOnlyMemory<PreparedOrchestrationGraph<TValue, TNumerics>> graphs,
            ReadOnlyMemory<OrchestrationProgram<TValue>> programs,
            ReadOnlyMemory<int>[] characterValueSlots,
            int[] dependencyIdentityTableIndices,
            TValue[] characterValueDefaults,
            OrchestrationBlackboardSlot[] characterValueDescriptors,
            OrchestrationValueSlotType[] characterValueTypes,
            ReadOnlyMemory<OrchestrationPreparedProgramStorage<TValue>> preparedProgramStorages, StableHash identity)
        {
            Identities = identities;
            Sources = sources;
            Graphs = graphs;
            Programs = programs;
            m_CharacterValueSlots = characterValueSlots;
            m_DependencyIdentityTableIndices = dependencyIdentityTableIndices;
            CharacterValueDefaults = characterValueDefaults;
            CharacterValueDescriptors = characterValueDescriptors;
            CharacterValueTypes = characterValueTypes;
            PreparedProgramStorages = preparedProgramStorages;
            Identity = identity;
        }

        public FrozenOrchestrationIdentities Identities { get; }
        public StableHash Identity { get; }
        public int ProgramCount => Programs.Length;
        public int DependencyIdentityTableCount => m_DependencyIdentityTableIndices.Length;
        public ReadOnlyMemory<OrchestrationProgramSource<TValue>> Sources { get; }
        public ReadOnlyMemory<PreparedOrchestrationGraph<TValue, TNumerics>> Graphs { get; }
        public ReadOnlyMemory<OrchestrationProgram<TValue>> Programs { get; }
        public ReadOnlyMemory<TValue> CharacterValueDefaults { get; }
        public ReadOnlyMemory<OrchestrationBlackboardSlot> CharacterValueDescriptors { get; }
        public ReadOnlyMemory<OrchestrationValueSlotType> CharacterValueTypes { get; }
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

        public static OrchestrationActorPreparation<TValue, TNumerics> Prepare(
            FrozenOrchestrationIdentities identities,
            ReadOnlySpan<OrchestrationProgramSource<TValue>> sources,
            ReadOnlySpan<int> skillContentIds,
            ReadOnlySpan<int> dependencyIdentityTableIndices)
        {
            var prepared = OrchestrationGraphPreparation<TValue, TNumerics>.Prepare(
                identities, sources, skillContentIds);

            var comparer = EqualityComparer<TValue>.Default;
            var characters = new SortedDictionary<(int Instance, int Declaration),
                (OrchestrationBlackboardSlot Descriptor, OrchestrationValueKind Kind, TValue DefaultValue, int Program)>();
            for (int programIndex = 0; programIndex < sources.Length; programIndex++)
            {
                OrchestrationGraphContent<TValue> content = sources[programIndex].Content;
                ReadOnlySpan<int> mapping = identities.LocalToShared(sources[programIndex].IdentityTableIndex).Span;
                foreach (ref readonly OrchestrationGraphBlackboardBinding binding in content.Structure.BlackboardBindings.Span)
                {
                    OrchestrationBlackboardSlot descriptor = content.Structure.Program.BlackboardSlots.Span[binding.ValueSlot];
                    if (descriptor.ScopeKind != OrchestrationScopeKind.Character)
                        continue;
                    OrchestrationValueKind kind = content.Structure.Program.ValueSlotTypes.Span[binding.ValueSlot].ValueKind;
                    TValue defaultValue = prepared.Graphs.Span[programIndex].Parameters.DefaultValues.Span[binding.ValueSlot];
                    var key = (Instance: mapping[binding.InstanceIdentityId], Declaration: mapping[binding.DeclarationIdentityId]);
                    if (characters.TryGetValue(key, out var existing))
                    {
                        if (existing.Kind != kind || existing.Descriptor.Lifetime != descriptor.Lifetime ||
                            existing.Descriptor.CompiledOwnerIndex != descriptor.CompiledOwnerIndex ||
                            !comparer.Equals(existing.DefaultValue, defaultValue))
                            throw new InvalidOperationException(
                                $"Character 黑板声明 '{identities.Table.Resolve(key.Declaration)}' 在图 " +
                                $"'{sources[existing.Program].Content.Identity.Source.GraphId}' 与 '{content.Identity.Source.GraphId}' " +
                                "中的类型、生命周期、所属 owner 或默认值不一致。");
                    }
                    else
                        characters.Add(key, (descriptor, kind, defaultValue, programIndex));
                }
            }

            var sharedSlots = new Dictionary<(int Instance, int Declaration), int>();
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
                    entry.Key.Instance, entry.Key.Declaration);
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
                var characterMapping = new int[content.Parameters.DefaultValues.Length];
                Array.Fill(characterMapping, -1);
                foreach (ref readonly OrchestrationGraphBlackboardBinding binding in content.Structure.BlackboardBindings.Span)
                {
                    OrchestrationBlackboardSlot descriptor = content.Structure.Program.BlackboardSlots.Span[binding.ValueSlot];
                    if (descriptor.ScopeKind == OrchestrationScopeKind.Character)
                    {
                        int shared = sharedSlots[(mapping[binding.InstanceIdentityId], mapping[binding.DeclarationIdentityId])];
                        characterMapping[binding.ValueSlot] = shared;
                    }
                }
                for (int valueSlot = 0; valueSlot < characterMapping.Length; valueSlot++)
                    if (content.Structure.Program.BlackboardSlots.Span[valueSlot].ScopeKind == OrchestrationScopeKind.Character &&
                        characterMapping[valueSlot] < 0)
                        throw new InvalidOperationException($"图 '{content.Identity.Source.GraphId}' 的 Character 黑板槽 '{valueSlot}' 缺少共享字段绑定。");
                characterMappings[programIndex] = characterMapping;
            }

            StableHash identity = OrchestrationIdentityContentHash.Actor(identities.Identity,
                sources, dependencyIdentityTableIndices,
                characterBlackboards, characterDescriptors, characterSlotTypes, characterKindSlotCounts, characterMappings);
            return new OrchestrationActorPreparation<TValue, TNumerics>(
                identities, sources.ToArray(), prepared.Graphs, prepared.Programs, characterMappings,
                dependencyIdentityTableIndices.ToArray(),
                characterDefaults, characterDescriptors, characterSlotTypes,
                prepared.PreparedProgramStorages, identity);
        }

    }
}
