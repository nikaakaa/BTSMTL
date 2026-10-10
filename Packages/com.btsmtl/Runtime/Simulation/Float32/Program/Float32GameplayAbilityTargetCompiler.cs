using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace ThirdPersonSimulation
{
    public sealed class Float32GameplayAbilityExecutionCompilationResult
    {
        readonly ReadOnlyCollection<Float32ScalarConversion> m_Conversions;

        public Float32GameplayAbilityExecutionCompilationResult(
            Float32GameplayAbilityExecutionData data,
            IEnumerable<Float32ScalarConversion> conversions)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            m_Conversions = new List<Float32ScalarConversion>(conversions ?? Array.Empty<Float32ScalarConversion>()).AsReadOnly();
        }

        public Float32GameplayAbilityExecutionData Data { get; }
        public IReadOnlyList<Float32ScalarConversion> Conversions => m_Conversions;
    }

    public static class Float32GameplayAbilityTargetCompiler
    {
        static readonly HashSet<string> s_SupportedGameplayCapabilities = new HashSet<string>(StringComparer.Ordinal)
        {
            "Action",
            "GameplayEffect",
            "PipelineBlackboard",
            "RunnableTree",
            "StateMachine",
            "Timeline",
            "TimelineScenePresentationParameter"
        };

        public static Float32GameplayAbilityExecutionCompilationResult Compile(ValidatedSemanticIrArtifact artifact)
        {
            if (artifact == null)
                throw new ArgumentNullException(nameof(artifact));
            GameplayAbilitySemanticIr semanticIr = artifact.SemanticIr;
            GameplayAbilitySemanticIrArtifactHeader header = artifact.Header;
            if (!semanticIr.Manifest.Root.IsAbility || !header.Root.IsAbility)
                throw new InvalidOperationException("Float32 Ability Target requires an Ability Semantic IR artifact.");
            if (!semanticIr.Manifest.ProgramId.Equals(header.ProgramId) ||
                !semanticIr.Manifest.SourceRevision.Equals(header.SourceRevision) ||
                !semanticIr.SemanticHash.Equals(header.SemanticHash))
            {
                throw new InvalidOperationException("Validated Semantic IR artifact identity is inconsistent.");
            }
            GameplayAbilityOperationSet.RequireVersion(header.OperationSetVersion);
            GameplayAbilityOperationSet.RequireCompleteBackend(
                header.OperationSetVersion,
                Float32SimulationTarget.Manifest.ExecutionTarget.SupportedOperations,
                Float32SimulationTarget.Manifest.ExecutionTarget.BackendIdentity);
            for (int i = 0; i < header.GameplayCapabilities.Count; i++)
            {
                if (!s_SupportedGameplayCapabilities.Contains(header.GameplayCapabilities[i]))
                    throw new InvalidOperationException($"Float32 Target does not support gameplay capability '{header.GameplayCapabilities[i]}'.");
            }
            ValidateLiteralPrecision(semanticIr);
            Float32GameplayAbilityExecutionCompilationResult result =
                Float32GameplayAbilityExecutionDataLowerer.Lower(semanticIr);
            if (!result.Data.Root.Equals(header.Root) ||
                !string.Equals(result.Data.CompilerVersion, header.CompilerVersion, StringComparison.Ordinal) ||
                !result.Data.OperationSetVersion.Equals(header.OperationSetVersion) ||
                result.Data.TickRate != header.TickRate ||
                !result.Data.SourceRevision.Equals(header.SourceRevision) ||
                !result.Data.SemanticHash.Equals(header.SemanticHash) ||
                result.Data.NumericProfile != Float32SimulationNumericProfile.Value)
            {
                throw new InvalidOperationException("Float32 Ability execution data does not preserve its Semantic IR artifact identity.");
            }
            return result;
        }

        static void ValidateLiteralPrecision(GameplayAbilitySemanticIr semanticIr)
        {
            for (int i = 0; i < semanticIr.Literals.Count; i++)
            {
                SemanticNumericPrecision precision = semanticIr.Literals[i].Precision;
                if (precision != SemanticNumericPrecision.Exact && precision != SemanticNumericPrecision.TargetRounded)
                    throw new InvalidOperationException($"Float32 Target does not support literal precision '{precision}' at '{semanticIr.Literals[i].Identity}'.");
            }
        }
    }

    static class Float32GameplayAbilityExecutionDataLowerer
    {
        internal static Float32GameplayAbilityExecutionCompilationResult Lower(GameplayAbilitySemanticIr semanticIr)
        {
            if (semanticIr == null)
                throw new ArgumentNullException(nameof(semanticIr));
            if (!semanticIr.Manifest.Root.IsAbility)
                throw new InvalidOperationException("Float32 Ability execution data requires an Ability root.");
            Float32SimulationTargetManifest target = Float32SimulationTarget.Manifest;
            if (target.Profile != Float32SimulationNumericProfile.Value)
                throw new InvalidOperationException("Float32 Numeric Target manifest is inconsistent.");
            GameplayAbilityOperationSet.RequireVersion(semanticIr.Manifest.OperationSetVersion);
            GameplayAbilityOperationSet.RequireCompleteBackend(
                semanticIr.Manifest.OperationSetVersion,
                target.ExecutionTarget.SupportedOperations,
                target.ExecutionTarget.BackendIdentity);

            for (int i = 0; i < semanticIr.Operations.Count; i++)
                GameplayAbilityOperationSet.RequireOperation(semanticIr.Operations[i].Code);
            var conversions = new List<Float32ScalarConversion>();
            Float32GraphDataLowerer.Lower(semanticIr.Operations, semanticIr.Literals, semanticIr.ConstantInputBindings,
                conversions, out List<SimulationOperationDefinition> definitions, out SimulationOperation[] operations,
                out ProgramConstant[] constants, out ProgramConstantInputBinding[] constantInputs);
            GameplayAbilityExecutionCatalog catalog = new GameplayAbilityExecutionCatalog(
                semanticIr.CatalogEntries,
                semanticIr.References);
            CharacterSkillId abilityId = RequireAbilityId(semanticIr.Manifest.Root.EntryIdentity);
            GameplayAbilityExecutionBinding binding = catalog.Require(abilityId);
            GameplayAbilityProviderContract providerContract = GameplayAbilityProviderContract.Create(
                semanticIr.CatalogEntries,
                index => constants[index].Int32);
            StableHash stateSchemaHash = StableHash.Compute(
                "float32-gameplay-ability-execution-layout/1",
                semanticIr.SemanticHash.ToString(),
                semanticIr.StateDeclarations.Count.ToString(CultureInfo.InvariantCulture),
                semanticIr.GraphCallFrames.Count.ToString(CultureInfo.InvariantCulture));
            Float32GameplayAbilityExecutionData data = Float32GameplayAbilityExecutionData.Create(
                abilityId,
                binding,
                providerContract,
                semanticIr.Manifest.CompilerVersion,
                semanticIr.Manifest.OperationSetVersion,
                semanticIr.Manifest.TickRate,
                semanticIr.Manifest.SourceRevision,
                semanticIr.SemanticHash,
                target.Profile,
                semanticIr.Manifest.Capabilities,
                semanticIr.Manifest.Root,
                semanticIr.Manifest.ProgramId.Value,
                stateSchemaHash,
                new Float32GraphExecutionData(
                    Float32GameplayAbilityExecutionData.ResolveRootOperation(semanticIr.References),
                    definitions, operations, constants, constantInputs, semanticIr.ControlFlow, semanticIr.References,
                    semanticIr.GraphCallFrames, semanticIr.StateDeclarations, semanticIr.Scopes,
                    semanticIr.OutputChannels, semanticIr.CatalogEntries, semanticIr.SourceMap, semanticIr.Producers));
            return new Float32GameplayAbilityExecutionCompilationResult(data, conversions);
        }

        static CharacterSkillId RequireAbilityId(string entryIdentity)
        {
            const string prefix = "ability:";
            if (string.IsNullOrEmpty(entryIdentity) || !entryIdentity.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidOperationException("Float32 Ability execution data entry identity is invalid.");
            return new CharacterSkillId(entryIdentity.Substring(prefix.Length));
        }

    }
}
