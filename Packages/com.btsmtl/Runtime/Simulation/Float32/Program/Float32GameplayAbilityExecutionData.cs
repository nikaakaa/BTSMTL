using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public sealed class Float32GameplayAbilityExecutionData : Float32GraphExecutionData
    {
        GameplayAbilityExecutionLayout m_ExecutionLayout;

        Float32GameplayAbilityExecutionData(
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
            Float32GraphExecutionData graph)
            : base(graph)
        {
            if (!root.IsAbility)
                throw new InvalidOperationException("Float32 Ability execution data requires an Ability root.");
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
        }

        public CharacterSkillId AbilityId { get; }
        public GameplayAbilityExecutionBinding Binding { get; private set; }

        internal Float32GameplayAbilityExecutionData WithInputBinding(in GameplayAbilityGrantInputBinding input)
        {
            GameplayAbilityExecutionBinding binding = Binding.WithInputBinding(in input);
            _ = ExecutionLayout;
            var installed = (Float32GameplayAbilityExecutionData)MemberwiseClone();
            installed.Binding = binding;
            return installed;
        }
        public GameplayAbilityExecutionLayout ExecutionLayout =>
            m_ExecutionLayout ??= Float32GameplayAbilityExecutionLayoutFactory.Create(this);
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
        internal static Float32GameplayAbilityExecutionData Create(
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
            Float32GraphExecutionData graph)
        {
            if (!abilityId.IsValid)
                throw new ArgumentException("Ability identity is invalid.", nameof(abilityId));
            return new Float32GameplayAbilityExecutionData(
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
                graph);
        }

        internal static OperationHandle ResolveRootOperation(IReadOnlyList<ProgramReference> references)
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

    }
}
