using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation.Fixed
{
    public sealed class FixedGameplayAbilityExecutionData
    {
        readonly AbilityStateValue[] m_DefaultStateValues;
        GameplayAbilityExecutionLayout m_ExecutionLayout;

        FixedGameplayAbilityExecutionData(
            CharacterSkillId abilityId,
            GameplayAbilityExecutionBinding binding,
            GameplayAbilityProviderContract providerContract,
            string compilerVersion,
            OperationSetVersion operationSetVersion,
            int tickRate,
            ProgramRevision sourceRevision,
            SemanticHash semanticHash,
            SimulationNumericProfile numericProfile,
            GameplayAbilityCapabilityManifest capabilities,
            GameplayAbilityRootDescriptor root,
            string executionIdentity,
            StableHash stateSchemaHash,
            IEnumerable<SimulationOperationDefinition> operationDefinitions,
            IEnumerable<SimulationOperation> operations,
            IEnumerable<ProgramConstant> constants,
            IEnumerable<ProgramConstantInputBinding> constantInputBindings,
            IEnumerable<ProgramControlFlowEdge> controlFlow,
            IEnumerable<ProgramReference> references,
            IEnumerable<ProgramGraphCallFrame> graphCallFrames,
            IEnumerable<ProgramStateSlot> stateSlots,
            IEnumerable<ProgramScopeLayout> scopes,
            IEnumerable<ProgramOutputChannelLayout> outputChannels,
            IEnumerable<ProgramCatalogEntry> catalogEntries,
            IEnumerable<ProgramSourceMapEntry> sourceMap,
            IEnumerable<ProgramProducer> producers)
        {
            if (!root.IsAbility)
                throw new InvalidOperationException("Fixed Ability execution data requires an Ability root.");
            AbilityId = abilityId;
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            ProviderContract = providerContract ?? throw new ArgumentNullException(nameof(providerContract));
            CompilerVersion = SimulationIdentity.Require(compilerVersion, nameof(compilerVersion));
            OperationSetVersion = operationSetVersion;
            TickRate = tickRate;
            SourceRevision = sourceRevision;
            SemanticHash = semanticHash;
            NumericProfile = numericProfile;
            Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            Root = root;
            ExecutionIdentity = SimulationIdentity.Require(executionIdentity, nameof(executionIdentity));
            StateSchemaHash = stateSchemaHash.IsValid
                ? stateSchemaHash
                : throw new ArgumentException("Ability state schema hash is invalid.", nameof(stateSchemaHash));
            ContentHash = StableHash.Compute(
                "gameplay-ability-execution-data/1",
                abilityId.Value,
                Root.ContentIdentity,
                ExecutionIdentity,
                StateSchemaHash.ToString());
            OperationDefinitions = Copy(operationDefinitions);
            Operations = Copy(operations);
            Constants = Copy(constants);
            ConstantInputBindings = Copy(constantInputBindings);
            ControlFlow = Copy(controlFlow);
            References = Copy(references);
            GraphCallFrames = Copy(graphCallFrames);
            StateSlots = Copy(stateSlots);
            Scopes = Copy(scopes);
            OutputChannels = Copy(outputChannels);
            CatalogEntries = Copy(catalogEntries);
            SourceMap = Copy(sourceMap);
            Producers = Copy(producers);
            CatalogIndex = new ProgramCatalogRuntimeIndex(Operations.Count, References, CatalogEntries);
            Topology = new OperationExecutionTopology(
                BuildOperationDescriptors(Operations),
                ControlFlow,
                References,
                StateSlots,
                SourceMap,
                ResolveRootOperation(References),
                GraphCallFrames);
            m_DefaultStateValues = new AbilityStateValue[StateSlots.Count];
            for (int index = 0; index < m_DefaultStateValues.Length; index++)
            {
                ProgramStateSlot slot = StateSlots[index];
                m_DefaultStateValues[index] = slot.DefaultConstantIndex >= 0
                    ? AbilityStateValue.FromConstant(Constants[slot.DefaultConstantIndex], slot.ValueKind)
                    : AbilityStateValue.Default(slot.ValueKind);
            }
        }

        public CharacterSkillId AbilityId { get; }
        public GameplayAbilityExecutionBinding Binding { get; private set; }

        internal FixedGameplayAbilityExecutionData WithInputBinding(in GameplayAbilityGrantInputBinding input)
        {
            GameplayAbilityExecutionBinding binding = Binding.WithInputBinding(in input);
            _ = ExecutionLayout;
            var installed = (FixedGameplayAbilityExecutionData)MemberwiseClone();
            installed.Binding = binding;
            return installed;
        }
        internal GameplayAbilityExecutionLayout ExecutionLayout =>
            m_ExecutionLayout ??= FixedGameplayAbilityExecutionLayoutFactory.Create(this);
        public GameplayAbilityProviderContract ProviderContract { get; }
        public string CompilerVersion { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public int TickRate { get; }
        public ProgramRevision SourceRevision { get; }
        public SemanticHash SemanticHash { get; }
        public SimulationNumericProfile NumericProfile { get; }
        public GameplayAbilityCapabilityManifest Capabilities { get; }
        public GameplayAbilityRootDescriptor Root { get; }
        public string ExecutionIdentity { get; }
        public StableHash StateSchemaHash { get; }
        public StableHash ContentHash { get; }
        public IReadOnlyList<SimulationOperationDefinition> OperationDefinitions { get; }
        public IReadOnlyList<SimulationOperation> Operations { get; }
        public IReadOnlyList<ProgramConstant> Constants { get; }
        public IReadOnlyList<ProgramConstantInputBinding> ConstantInputBindings { get; }
        public IReadOnlyList<ProgramControlFlowEdge> ControlFlow { get; }
        public IReadOnlyList<ProgramReference> References { get; }
        public IReadOnlyList<ProgramGraphCallFrame> GraphCallFrames { get; }
        public IReadOnlyList<ProgramStateSlot> StateSlots { get; }
        public IReadOnlyList<ProgramScopeLayout> Scopes { get; }
        public IReadOnlyList<ProgramOutputChannelLayout> OutputChannels { get; }
        public AbilityStateValue DefaultStateValue(int slotIndex) => m_DefaultStateValues[slotIndex];

        public IReadOnlyList<ProgramCatalogEntry> CatalogEntries { get; }
        public IReadOnlyList<ProgramSourceMapEntry> SourceMap { get; }
        public IReadOnlyList<ProgramProducer> Producers { get; }
        public ProgramCatalogRuntimeIndex CatalogIndex { get; }
        public OperationExecutionTopology Topology { get; }
        public OperationHandle RootOperation => Topology.RootOperation;

        internal static FixedGameplayAbilityExecutionData Create(
            CharacterSkillId abilityId,
            GameplayAbilityExecutionBinding binding,
            GameplayAbilityProviderContract providerContract,
            string compilerVersion,
            OperationSetVersion operationSetVersion,
            int tickRate,
            ProgramRevision sourceRevision,
            SemanticHash semanticHash,
            SimulationNumericProfile numericProfile,
            GameplayAbilityCapabilityManifest capabilities,
            GameplayAbilityRootDescriptor root,
            string executionIdentity,
            StableHash stateSchemaHash,
            IEnumerable<SimulationOperationDefinition> operationDefinitions,
            IEnumerable<SimulationOperation> operations,
            IEnumerable<ProgramConstant> constants,
            IEnumerable<ProgramConstantInputBinding> constantInputBindings,
            IEnumerable<ProgramControlFlowEdge> controlFlow,
            IEnumerable<ProgramReference> references,
            IEnumerable<ProgramGraphCallFrame> graphCallFrames,
            IEnumerable<ProgramStateSlot> stateSlots,
            IEnumerable<ProgramScopeLayout> scopes,
            IEnumerable<ProgramOutputChannelLayout> outputChannels,
            IEnumerable<ProgramCatalogEntry> catalogEntries,
            IEnumerable<ProgramSourceMapEntry> sourceMap,
            IEnumerable<ProgramProducer> producers)
        {
            if (!abilityId.IsValid)
                throw new ArgumentException("Ability identity is invalid.", nameof(abilityId));
            return new FixedGameplayAbilityExecutionData(
                abilityId,
                binding,
                providerContract,
                compilerVersion,
                operationSetVersion,
                tickRate,
                sourceRevision,
                semanticHash,
                numericProfile,
                capabilities,
                root,
                executionIdentity,
                stateSchemaHash,
                operationDefinitions,
                operations,
                constants,
                constantInputBindings,
                controlFlow,
                references,
                graphCallFrames,
                stateSlots,
                scopes,
                outputChannels,
                catalogEntries,
                sourceMap,
                producers);
        }

        static IReadOnlyList<OperationExecutionDescriptor> BuildOperationDescriptors(
            IReadOnlyList<SimulationOperation> operations)
        {
            var result = new OperationExecutionDescriptor[operations.Count];
            for (int i = 0; i < operations.Count; i++)
            {
                SimulationOperation operation = operations[i];
                result[i] = new OperationExecutionDescriptor(
                    operation.Handle,
                    operation.Code,
                    operation.Integer0,
                    operation.Integer1,
                    operation.Unsigned0,
                    operation.Text0,
                    operation.Flags,
                    operation.StateSlots);
            }
            return result;
        }

        static OperationHandle ResolveRootOperation(IReadOnlyList<ProgramReference> references)
        {
            for (int i = 0; i < references.Count; i++)
            {
                ProgramReference reference = references[i];
                if (!reference.HasSourceOperation &&
                    reference.Kind == ProgramReferenceKind.Operation &&
                    string.Equals(reference.Identity, "ability:root-operation", StringComparison.Ordinal))
                    return new OperationHandle(reference.TargetIndex);
            }
            throw new InvalidOperationException("Ability root operation reference is missing.");
        }

        static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) =>
            new ReadOnlyCollection<T>(new List<T>(values ?? Array.Empty<T>()));
    }
}
