using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace ThirdPersonSimulation
{
    public sealed class Float32GameplayAbilityExecutionCompilationResult
    {
        readonly ReadOnlyCollection<Float32ScalarConversion> m_Conversions;

        public Float32GameplayAbilityExecutionCompilationResult(
            Float32GameplayAbilityExecutionData data,
            IEnumerable<Float32ScalarConversion> conversions, GraphCompilationWork work)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Work = work;
            m_Conversions = new List<Float32ScalarConversion>(conversions ?? Array.Empty<Float32ScalarConversion>()).AsReadOnly();
        }

        internal Float32GameplayAbilityExecutionCompilationResult(Float32GameplayAbilityExecutionCompilationResult previous, Float32GameplayAbilityExecutionData data, GraphCompilationWork work)
        {
            Data = data;
            Work = work;
            m_Conversions = previous.m_Conversions;
        }

        public GraphCompilationWork Work { get; }
        public Float32GameplayAbilityExecutionData Data { get; }
        public IReadOnlyList<Float32ScalarConversion> Conversions => m_Conversions;
    }

    public static class Float32GameplayAbilityTargetCompiler
    {
        sealed class CachedTarget
        {
            internal readonly GameplayAbilitySemanticIr Ir;
            internal readonly Float32GameplayAbilityExecutionCompilationResult Result;

            internal CachedTarget(GameplayAbilitySemanticIr ir, Float32GameplayAbilityExecutionCompilationResult result)
            {
                Ir = ir;
                Result = result;
            }
        }

        static readonly ConditionalWeakTable<GraphExecutionChunk, CachedTarget> s_Targets = new();

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

        public static Float32GameplayAbilityExecutionCompilationResult Compile(GameplayAbilitySemanticIr semanticIr)
        {
            Float32GameplayAbilityExecutionCompilationResult result;
            GraphExecutionChunk structure = semanticIr.Chunks.Structure;
            if (s_Targets.TryGetValue(structure, out CachedTarget cached))
            {
                if (ReferenceEquals(cached.Ir, semanticIr))
                    return new Float32GameplayAbilityExecutionCompilationResult(cached.Result, cached.Result.Data, GraphCompilationWork.Unchanged);
                if (cached.Ir.Chunks.Parameters.Hash == semanticIr.Chunks.Parameters.Hash)
                {
                    Float32GameplayAbilityExecutionData data = Float32GameplayAbilityExecutionDataLowerer.Bind(semanticIr, cached.Result.Data,
                        cached.Result.Data.WithSources(semanticIr.GraphSources));
                    result = new Float32GameplayAbilityExecutionCompilationResult(cached.Result, data, GraphCompilationWork.Sources);
                }
                else
                    result = Float32GameplayAbilityExecutionDataLowerer.LowerParameters(semanticIr, cached.Result.Data);
            }
            else
            {
                IReadOnlyList<string> capabilities = semanticIr.Manifest.Capabilities.GameplayCapabilities;
                for (int i = 0; i < capabilities.Count; i++)
                {
                    if (!s_SupportedGameplayCapabilities.Contains(capabilities[i]))
                        throw new InvalidOperationException($"Float32 Target does not support gameplay capability '{capabilities[i]}'.");
                }
                result = Float32GameplayAbilityExecutionDataLowerer.Lower(semanticIr);
            }
            s_Targets.Remove(structure);
            s_Targets.Add(structure, new CachedTarget(semanticIr, result));
            return result;
        }

    }

    static class Float32GameplayAbilityExecutionDataLowerer
    {
        internal static Float32GameplayAbilityExecutionCompilationResult LowerParameters(GameplayAbilitySemanticIr semanticIr, Float32GameplayAbilityExecutionData structure)
        {
            var conversions = new List<Float32ScalarConversion>();
            Float32GraphExecutionData graph = Float32GraphDataLowerer.LowerParameters(semanticIr, structure, conversions);
            return new Float32GameplayAbilityExecutionCompilationResult(Bind(semanticIr, structure, graph), conversions, GraphCompilationWork.Parameters);
        }

        internal static Float32GameplayAbilityExecutionData Bind(GameplayAbilitySemanticIr semanticIr, Float32GameplayAbilityExecutionData previous,
            Float32GraphExecutionData graph) =>
            Float32GameplayAbilityExecutionData.Create(previous.AbilityId, previous.Binding, previous.ProviderContract,
                semanticIr.Manifest.CompilerVersion, semanticIr.Manifest.OperationSetVersion, semanticIr.Manifest.TickRate,
                semanticIr.Manifest.SourceRevision, semanticIr.SemanticHash, previous.NumericProfile,
                semanticIr.Manifest.Capabilities, semanticIr.Manifest.Root, semanticIr.Manifest.ProgramId.Value,
                previous.StateSchemaHash, graph);

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
                "float32-gameplay-ability-execution-layout/2",
                semanticIr.StateLayoutHash.ToString(),
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
                    semanticIr.OutputChannels, semanticIr.CatalogEntries, semanticIr.GraphSources, semanticIr.Producers));
            return new Float32GameplayAbilityExecutionCompilationResult(data, conversions, GraphCompilationWork.Structure);
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
