using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation.FPGameplay
{
    public sealed class FPGameplayAbilityExecutionData : FPGraphExecutionData
    {
        GameplayAbilityExecutionLayout m_ExecutionLayout;

        public FPGameplayAbilityExecutionData(GameplayAbilityExecutionManifest manifest,
            GameplayAbilityExecutionBinding binding, GameplayAbilityProviderContract providerContract,
            FPGraphExecutionData graph)
            : base(graph)
        {
            Manifest = manifest;
            Binding = binding ?? throw new ArgumentNullException(nameof(binding));
            ProviderContract = providerContract ?? throw new ArgumentNullException(nameof(providerContract));
        }

        public GameplayAbilityExecutionManifest Manifest { get; }
        public CharacterSkillId AbilityId => Manifest.AbilityId;
        public GameplayAbilityExecutionBinding Binding { get; private set; }

        internal FPGameplayAbilityExecutionData WithInputBinding(in GameplayAbilityGrantInputBinding input)
        {
            GameplayAbilityExecutionBinding binding = Binding.WithInputBinding(in input);
            _ = ExecutionLayout;
            var installed = (FPGameplayAbilityExecutionData)MemberwiseClone();
            installed.Binding = binding;
            return installed;
        }
        internal GameplayAbilityExecutionLayout ExecutionLayout =>
            m_ExecutionLayout ??= FPGameplayAbilityExecutionLayoutFactory.Create(this);
        public GameplayAbilityProviderContract ProviderContract { get; }
        public string CompilerVersion => Manifest.CompilerVersion;
        public OperationSetVersion OperationSetVersion => Manifest.OperationSetVersion;
        public int TickRate => Manifest.TickRate;
        public ProgramRevision SourceRevision => Manifest.SourceRevision;
        public SemanticHash SemanticHash => Manifest.SemanticHash;
        public SimulationNumericProfile NumericProfile => Manifest.NumericProfile;
        public GameplayAbilityCapabilityManifest Capabilities => Manifest.Capabilities;
        public GameplayAbilityRootDescriptor Root => Manifest.Root;
        public string ExecutionIdentity => Manifest.ExecutionIdentity;
        public StableHash StateSchemaHash => Manifest.StateSchemaHash;
        public StableHash ContentHash => Manifest.ContentHash;
        internal static FPGameplayAbilityExecutionData Create(
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
            FPGraphExecutionData graph)
        {
            var manifest = new GameplayAbilityExecutionManifest(abilityId, compilerVersion,
                operationSetVersion, tickRate, sourceRevision, semanticHash, numericProfile, capabilities,
                root, executionIdentity, stateSchemaHash,
                graph.GraphSources.Layout.Publisher.ComputeContentHash(graph.Chunks, semanticHash, numericProfile, tickRate, abilityId.Value));
            return new FPGameplayAbilityExecutionData(manifest, binding, providerContract, graph);
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
