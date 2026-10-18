using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using BTSMTL.Compilation;
using BTSMTL.Orchestration;

namespace ThirdPersonSimulation
{
    public sealed partial class GameplayAbilitySemanticIrContract
    {
        public GameplayAbilitySemanticIrContract(ProgramId programId, string compilerVersion,
            OperationSetVersion operationSetVersion, int tickRate,
            GameplayAbilityRootDescriptor root)
        {
            ProgramId = programId;
            CompilerVersion = SimulationIdentity.Require(compilerVersion, nameof(compilerVersion));
            OperationSetVersion = operationSetVersion;
            TickRate = tickRate;
            Root = root;
            RequireRecord();
        }

        public ProgramId ProgramId { get; }
        public string CompilerVersion { get; }
        public OperationSetVersion OperationSetVersion { get; }
        public int TickRate { get; }
        public GameplayAbilityRootDescriptor Root { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (!ProgramId.IsValid || !OperationSetVersion.IsValid || TickRate <= 0)
                throw new ArgumentException("Semantic IR contract is incomplete.");
            SimulationIdentity.Require(CompilerVersion, nameof(CompilerVersion));
            if (!Root.IsValid || !Root.IsAbility)
                throw new ArgumentException("Semantic IR requires an Ability root descriptor.", nameof(Root));
        }
    }

    public sealed partial class GameplayAbilitySemanticIrManifest
    {
        public GameplayAbilitySemanticIrManifest(ProgramId programId, string compilerVersion,
            OperationSetVersion operationSetVersion, int tickRate, ProgramRevision sourceRevision,
            GameplayAbilityRootDescriptor root)
        {
            Contract = new GameplayAbilitySemanticIrContract(programId, compilerVersion,
                operationSetVersion, tickRate, root);
            SourceRevision = sourceRevision;
            RequireRecord();
        }

        public GameplayAbilitySemanticIrContract Contract { get; }
        public ProgramRevision SourceRevision { get; }
        public ProgramId ProgramId => Contract.ProgramId;
        public string CompilerVersion => Contract.CompilerVersion;
        public OperationSetVersion OperationSetVersion => Contract.OperationSetVersion;
        public int TickRate => Contract.TickRate;
        public GameplayAbilityRootDescriptor Root => Contract.Root;

        [ValueValidation]
        void RequireRecord()
        {
            if (string.IsNullOrEmpty(SourceRevision.Value))
                throw new ArgumentException("Semantic IR source revision is required.");
        }
    }

    public sealed class GameplayAbilitySemanticIr
    {
        readonly ReadOnlyCollection<SemanticOperation> m_Operations;
        SemanticGraphStructure Structure => Tables.Structure;
        readonly SemanticStateLayout m_StateLayout;
        readonly int[] m_StateSlotByOperation;

        public GameplayAbilitySemanticIr(
            GameplayAbilitySemanticIrManifest manifest,
            IEnumerable<SemanticOperation> operations,
            IEnumerable<SemanticLiteral> literals,
            IEnumerable<SemanticConstantInputBinding> constantInputBindings,
            IEnumerable<ProgramControlFlowEdge> controlFlow,
            IEnumerable<ProgramReference> references,
            IEnumerable<ProgramStateSlot> stateDeclarations,
            IEnumerable<ProgramScopeLayout> scopes,
            IEnumerable<ProgramOutputChannelLayout> outputChannels,
            IEnumerable<ProgramCatalogEntry> catalogEntries,
            ProgramGraphSources graphSources,
            IEnumerable<ProgramProducer> producers,
            IEnumerable<ProgramGraphCallFrame> graphCallFrames = null)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            m_Operations = Indexed(operations, value => value.Handle.Value, "semantic operation");
            var parameters = new SemanticGraphParameters(Indexed(literals, value => value.Index, "semantic literal"),
                m_Operations[0].ParameterValues);
            var callFrames = Indexed(graphCallFrames, value => value.Index, "graph call frame");
            var structure = new SemanticGraphStructure(m_Operations,
                SortConstantInputs(constantInputBindings, m_Operations, callFrames),
                ByIdentity(controlFlow, value => value.Identity, "control-flow edge"),
                ByIdentity(references, value => value.Identity, "reference"),
                new SemanticStateTables(Indexed(stateDeclarations, value => value.Index, "state declaration"),
                    ByIdentity(scopes, value => value.Identity, "scope"), callFrames),
                Indexed(outputChannels, value => value.Index, "output channel"),
                Indexed(catalogEntries, value => value.Index, "catalog entry"),
                Indexed(producers, value => value.Index, "producer"));
            Tables = new SemanticGraphTables(structure, parameters);
            m_StateLayout = new SemanticStateLayout(Structure.StateTables, m_Operations);
            GraphSources = graphSources;
            m_StateSlotByOperation = new int[m_Operations.Count];
            Array.Fill(m_StateSlotByOperation, -1);
            ProgramExecutionSourceCoverage.Require(
                ExecutionSources,
                m_Operations.Count,
                m_StateLayout.Slots.Count);
            GameplayAbilityRootValidation.RequireEntryReference(
                Manifest.Root,
                Structure.References,
                m_Operations);
            ValidateReferences();
            ValidateGraphCallFrames();
        }

        GameplayAbilitySemanticIr(GameplayAbilitySemanticIr structure, GameplayAbilitySemanticIrManifest manifest,
            SemanticGraphParameters parameters, ProgramGraphSources sources)
        {
            Manifest = manifest;
            Tables = new SemanticGraphTables(structure.Tables.Structure, parameters);
            if (ReferenceEquals(parameters, structure.Parameters))
                m_Operations = structure.m_Operations;
            else
            {
                var operations = new SemanticOperation[structure.Operations.Count];
                for (int i = 0; i < operations.Length; i++)
                    operations[i] = new SemanticOperation(structure.Operations[i], parameters.Operations);
                m_Operations = Array.AsReadOnly(operations);
            }
            m_StateLayout = structure.m_StateLayout;
            m_StateSlotByOperation = structure.m_StateSlotByOperation;
            GraphSources = sources;
        }

        public GameplayAbilitySemanticIr WithParameters(GameplayAbilitySemanticIrManifest manifest,
            SemanticGraphParameters parameters, ProgramGraphSources sources) =>
            new GameplayAbilitySemanticIr(this, manifest, parameters, sources);

        public GameplayAbilitySemanticIr WithSources(GameplayAbilitySemanticIrManifest manifest,
            ProgramGraphSources sources) => new GameplayAbilitySemanticIr(this, manifest, Parameters, sources);

        public GameplayAbilitySemanticIrManifest Manifest { get; }
        public IReadOnlyList<SemanticOperation> Operations => m_Operations;
        public SemanticGraphParameters Parameters => Tables.Parameters;
        public IReadOnlyList<SemanticLiteral> Literals => Parameters.Literals;
        public IReadOnlyList<SemanticConstantInputBinding> ConstantInputBindings => Structure.ConstantInputBindings;
        public IReadOnlyList<ProgramControlFlowEdge> ControlFlow => Structure.ControlFlow;
        public IReadOnlyList<ProgramReference> References => Structure.References;
        public IReadOnlyList<ProgramGraphCallFrame> GraphCallFrames => m_StateLayout.GraphCallFrames;
        public IReadOnlyList<ProgramStateSlot> StateDeclarations => m_StateLayout.Slots;
        public IReadOnlyList<ProgramScopeLayout> Scopes => m_StateLayout.Scopes;
        public IReadOnlyList<ProgramOutputChannelLayout> OutputChannels => Structure.OutputChannels;
        public IReadOnlyList<ProgramCatalogEntry> CatalogEntries => Structure.CatalogEntries;
        public ProgramGraphSources GraphSources { get; }
        public IReadOnlyList<ProgramSourceMapEntry> SourceMap => GraphSources.Diagnostics;
        public ProgramExecutionSourceLayout ExecutionSourceLayout => GraphSources.Layout;
        public IReadOnlyList<ProgramExecutionSource> ExecutionSources => GraphSources.Layout.Entries;
        public ProgramGraphSourceRevisionTable GraphSourceRevisions => GraphSources.Revisions;
        public IReadOnlyList<ProgramProducer> Producers => Structure.Producers;
        public SemanticStateLayout StateLayout => m_StateLayout;
        public SemanticGraphTables Tables { get; }

        void ValidateGraphCallFrames()
        {
            var operationCodes = new OrchestrationOperationCode[m_Operations.Count];
            for (int i = 0; i < m_StateLayout.GraphCallFrames.Count; i++)
            {
                ProgramGraphCallFrame frame = m_StateLayout.GraphCallFrames[i];
                if (frame.OwnerOperation.Value >= m_Operations.Count || frame.EntryOperation.Value >= m_Operations.Count)
                    throw new InvalidDataException($"Graph call frame '{frame.Identity}' references an operation outside the table.");
                ValidateGraphParameterBindings(frame, frame.Inputs);
                ValidateGraphParameterBindings(frame, frame.Outputs);
            }
            for (int i = 0; i < m_Operations.Count; i++)
                operationCodes[i] = m_Operations[i].Code;
            ProgramGraphCallFrameContract.ValidateCoverage(operationCodes, m_StateLayout.GraphCallFrames);
        }

        void ValidateGraphParameterBindings(
            ProgramGraphCallFrame frame,
            IReadOnlyList<ProgramGraphParameterBinding> bindings)
        {
            for (int i = 0; i < bindings.Count; i++)
            {
                ProgramGraphParameterBinding binding = bindings[i];
                if (binding.StateSlot >= m_StateLayout.Slots.Count)
                    throw new InvalidDataException($"Graph call frame '{frame.Identity}' parameter '{binding.ParameterName}' references an unknown state slot.");
                SemanticValueKind actualKind = OrchestrationValuePortContracts.FromState(
                    m_StateLayout.Slots[binding.StateSlot].ValueKind);
                if (actualKind != binding.ValueKind)
                    throw new InvalidDataException($"Graph call frame '{frame.Identity}' parameter '{binding.ParameterName}' state kind '{actualKind}' does not match '{binding.ValueKind}'.");
            }
        }

        public SemanticValueKind ResolveLinkedValueKind(ProgramControlFlowEdge edge)
        {
            if (edge == null || edge.Kind != ProgramControlFlowKind.Value)
                throw new ArgumentException("A Value control-flow edge is required.", nameof(edge));
            if (edge.Source.Value < 0 || edge.Source.Value >= m_Operations.Count ||
                edge.Target.Value < 0 || edge.Target.Value >= m_Operations.Count)
                throw new InvalidDataException($"Value edge '{edge.Identity}' references an operation outside the table.");
            SemanticOperation source = m_Operations[edge.Source.Value];
            OperationValuePortDefinition sourcePort = OrchestrationValuePortContracts
                .Require(source, m_StateLayout.GraphCallFrames)
                .RequireSelection(edge.SourcePort);
            return ResolveOutputKind(source, sourcePort);
        }

        void ValidateReferences()
        {
            GameplayAbilityOperationSet.RequireVersion(Manifest.OperationSetVersion);
            for (int i = 0; i < m_Operations.Count; i++)
            {
                SemanticOperation operation = m_Operations[i];
                OrchestrationOperationSet.RequireOperation(operation.Code);
                ValidateIndexes(operation.Operands, m_Operations.Count, $"Operation '{operation.Handle}' operand");
                ValidateIndexes(operation.LiteralReferences, Parameters.Literals.Count, $"Operation '{operation.Handle}' literal");
                ValidateIndexes(operation.StateSlots, m_StateLayout.Slots.Count, $"Operation '{operation.Handle}' state");
            }
            for (int i = 0; i < m_StateLayout.Slots.Count; i++)
            {
                int index = m_StateLayout.Slots[i].DefaultConstantIndex;
                if (index < -1 || index >= Parameters.Literals.Count)
                    throw new InvalidDataException($"State declaration '{m_StateLayout.Slots[i].Identity}' has invalid default literal '{index}'.");
            }
            for (int i = 0; i < Structure.References.Count; i++)
            {
                ProgramReference reference = Structure.References[i];
                if (reference.HasSourceOperation &&
                    (reference.SourceOperation.Value < 0 || reference.SourceOperation.Value >= m_Operations.Count))
                {
                    throw new InvalidDataException($"Reference '{reference.Identity}' has an unknown source operation.");
                }
                if (reference.Kind == ProgramReferenceKind.StateSlot && reference.HasSourceOperation &&
                    m_StateSlotByOperation[reference.SourceOperation.Value] < 0)
                    m_StateSlotByOperation[reference.SourceOperation.Value] = reference.TargetIndex;
            }
            var valueSources = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < Structure.ControlFlow.Count; i++)
            {
                ProgramControlFlowEdge edge = Structure.ControlFlow[i];
                if (edge.Source.Value < 0 || edge.Source.Value >= m_Operations.Count ||
                    edge.Target.Value < 0 || edge.Target.Value >= m_Operations.Count)
                    throw new InvalidDataException($"Control-flow edge '{edge.Identity}' references an operation outside the table.");
                SemanticOperation source = m_Operations[edge.Source.Value];
                if (edge.Kind != ProgramControlFlowKind.Value)
                    continue;
                SemanticOperation target = m_Operations[edge.Target.Value];
                OperationValuePortDefinition sourcePort = OrchestrationValuePortContracts
                    .Require(source, m_StateLayout.GraphCallFrames)
                    .RequireSelection(edge.SourcePort);
                OperationValuePortDefinition targetPort = OrchestrationValuePortContracts
                    .Require(target, m_StateLayout.GraphCallFrames)
                    .RequireInput(edge.TargetPort);
                SemanticValueKind sourceKind = ResolveOutputKind(source, sourcePort);
                SemanticValueKind targetKind = ResolveInputKind(target, targetPort, sourceKind);
                if (!targetPort.Accepts(sourceKind) || !KindsCanFlow(sourceKind, targetKind, targetPort.Constraint))
                    throw new InvalidDataException($"Value edge '{edge.Identity}' cannot assign '{sourceKind}' to '{target.Code}.{targetPort.Identity}' ({targetKind}).");
                string key = InputKey(edge.Target.Value, edge.TargetPort);
                if (!valueSources.Add(key))
                    throw new InvalidDataException($"Operation '{edge.Target}' input port '{edge.TargetPort}' has multiple linked Value sources.");
            }
            for (int i = 0; i < Structure.ConstantInputBindings.Count; i++)
            {
                SemanticConstantInputBinding binding = Structure.ConstantInputBindings[i];
                if (binding.TargetOperation.Value < 0 || binding.TargetOperation.Value >= m_Operations.Count)
                    throw new InvalidDataException($"Constant input binding targets unknown operation '{binding.TargetOperation}'.");
                if (binding.ConstantIndex < 0 || binding.ConstantIndex >= Parameters.Literals.Count)
                    throw new InvalidDataException($"Constant input binding targets unknown literal '{binding.ConstantIndex}'.");
                SemanticOperation target = m_Operations[binding.TargetOperation.Value];
                OperationValuePortDefinition port = OrchestrationValuePortContracts
                    .Require(target, m_StateLayout.GraphCallFrames)
                    .RequireInput(binding.TargetPort);
                SemanticValueKind literalKind = OrchestrationValuePortContracts.FromLiteral(Parameters.Literals[binding.ConstantIndex].Kind);
                SemanticValueKind expectedKind = ResolveInputKind(target, port, binding.ResolvedValueKind);
                if (binding.ResolvedValueKind != literalKind ||
                    !port.Accepts(binding.ResolvedValueKind) ||
                    !KindsCanFlow(binding.ResolvedValueKind, expectedKind, port.Constraint))
                    throw new InvalidDataException($"Constant input '{target.Code}.{binding.TargetPort}' has incompatible kind '{binding.ResolvedValueKind}'.");
                string key = InputKey(binding.TargetOperation.Value, binding.TargetPort);
                if (!valueSources.Add(key))
                    throw new InvalidDataException($"Operation '{binding.TargetOperation}' input port '{binding.TargetPort}' has both linked and constant Value sources.");
            }
        }

        SemanticValueKind ResolveOutputKind(SemanticOperation operation, OperationValuePortDefinition port)
        {
            if (port.Constraint == OperationValuePortConstraint.Fixed)
                return port.FixedKind;
            if (operation.Code == OrchestrationOperationCode.BlackboardGet)
                return ResolveBlackboardKind(operation);
            if (operation.Code == OrchestrationOperationCode.Constant && operation.LiteralReferences.Count > 0)
                return OrchestrationValuePortContracts.FromLiteral(Parameters.Literals[operation.LiteralReferences[0]].Kind);
            throw new InvalidDataException($"Value output '{operation.Code}.{port.Identity}' cannot resolve a concrete kind.");
        }

        SemanticValueKind ResolveInputKind(
            SemanticOperation operation,
            OperationValuePortDefinition port,
            SemanticValueKind actualKind)
        {
            if (port.Constraint == OperationValuePortConstraint.Dynamic)
            {
                if (operation.Code == OrchestrationOperationCode.BlackboardSet)
                    return ResolveBlackboardKind(operation);
                throw new InvalidDataException($"Value input '{operation.Code}.{port.Identity}' cannot resolve a concrete kind.");
            }
            return port.Resolve(actualKind);
        }

        SemanticValueKind ResolveBlackboardKind(SemanticOperation operation)
        {
            int stateSlot = m_StateSlotByOperation[operation.Handle.Value];
            if (stateSlot < 0)
                throw new InvalidDataException($"Blackboard operation '{operation.Handle}' has no compiled state address.");
            return OrchestrationValuePortContracts.FromState(m_StateLayout.Slots[stateSlot].ValueKind);
        }

        static bool KindsCanFlow(
            SemanticValueKind source,
            SemanticValueKind target,
            OperationValuePortConstraint constraint)
        {
            if (source == target)
                return true;
            return constraint switch
            {
                OperationValuePortConstraint.BooleanLike => OperationValuePortDefinition.IsBooleanLike(source),
                OperationValuePortConstraint.NumericLike => OperationValuePortDefinition.IsNumericLike(source) && OperationValuePortDefinition.IsNumericLike(target),
                _ => false
            };
        }

        static string InputKey(int operation, string port) => operation.ToString() + ":" + (port ?? string.Empty);

        static void ValidateIndexes(IReadOnlyList<int> values, int count, string label)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] < 0 || values[i] >= count)
                    throw new InvalidDataException($"{label} reference '{values[i]}' is outside the table.");
            }
        }

        static ReadOnlyCollection<T> Indexed<T>(IEnumerable<T> source, Func<T, int> index, string label)
        {
            var values = new List<T>(source ?? Array.Empty<T>());
            values.Sort((left, right) => index(left).CompareTo(index(right)));
            for (int i = 0; i < values.Count; i++)
            {
                if (index(values[i]) != i)
                    throw new InvalidDataException($"{label} table must contain contiguous canonical indexes.");
            }
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<T> ByIdentity<T>(IEnumerable<T> source, Func<T, string> identity, string label)
        {
            var values = new List<T>(source ?? Array.Empty<T>());
            values.Sort((left, right) => string.CompareOrdinal(identity(left), identity(right)));
            for (int i = 1; i < values.Count; i++)
            {
                if (string.Equals(identity(values[i - 1]), identity(values[i]), StringComparison.Ordinal))
                    throw new InvalidDataException($"{label} identity '{identity(values[i])}' is duplicated.");
            }
            return values.AsReadOnly();
        }

        static ReadOnlyCollection<SemanticConstantInputBinding> SortConstantInputs(
            IEnumerable<SemanticConstantInputBinding> source,
            IReadOnlyList<SemanticOperation> operations,
            IReadOnlyList<ProgramGraphCallFrame> graphCallFrames)
        {
            var values = new List<SemanticConstantInputBinding>(source ?? Array.Empty<SemanticConstantInputBinding>());
            var contracts = new Dictionary<int, OperationValuePortContract>();
            values.Sort((left, right) =>
            {
                int operation = left.TargetOperation.Value;
                int byOperation = operation.CompareTo(right.TargetOperation.Value);
                if (byOperation != 0)
                    return byOperation;
                if (operation < 0 || operation >= operations.Count)
                    return string.CompareOrdinal(left.TargetPort, right.TargetPort);
                if (!contracts.TryGetValue(operation, out OperationValuePortContract contract))
                {
                    contract = OrchestrationValuePortContracts.Require(operations[operation], graphCallFrames);
                    contracts.Add(operation, contract);
                }
                OperationValuePortDefinition leftPort = contract.RequireInput(left.TargetPort);
                OperationValuePortDefinition rightPort = contract.RequireInput(right.TargetPort);
                int byOrder = leftPort.Order.CompareTo(rightPort.Order);
                return byOrder != 0 ? byOrder : string.CompareOrdinal(left.TargetPort, right.TargetPort);
            });
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == null)
                    throw new InvalidDataException("Constant input binding table contains null.");
                if (i > 0 && values[i - 1].TargetOperation.Equals(values[i].TargetOperation) &&
                    string.Equals(values[i - 1].TargetPort, values[i].TargetPort, StringComparison.Ordinal))
                    throw new InvalidDataException($"Constant input binding '{values[i].TargetOperation}/{values[i].TargetPort}' is duplicated.");
            }
            return values.AsReadOnly();
        }

    }
    [ValueStorage]
    public sealed partial class SemanticGraphStructure
    {
        internal SemanticGraphStructure(IReadOnlyList<SemanticOperation> operations,
            ReadOnlyCollection<SemanticConstantInputBinding> constantInputBindings,
            ReadOnlyCollection<ProgramControlFlowEdge> controlFlow, ReadOnlyCollection<ProgramReference> references,
            SemanticStateTables stateTables, ReadOnlyCollection<ProgramOutputChannelLayout> outputChannels,
            ReadOnlyCollection<ProgramCatalogEntry> catalogEntries,
            ReadOnlyCollection<ProgramProducer> producers)
        {
            var layouts = new SemanticOperationLayout[operations.Count];
            for (int i = 0; i < layouts.Length; i++)
                layouts[i] = operations[i].Layout;
            OperationLayouts = Array.AsReadOnly(layouts);
            ConstantInputBindings = constantInputBindings;
            ControlFlow = controlFlow;
            References = references;
            StateTables = stateTables;
            OutputChannels = outputChannels;
            CatalogEntries = catalogEntries;
            Producers = producers;
        }

        public IReadOnlyList<SemanticOperationLayout> OperationLayouts { get; }
        public ReadOnlyCollection<SemanticConstantInputBinding> ConstantInputBindings { get; }
        public ReadOnlyCollection<ProgramControlFlowEdge> ControlFlow { get; }
        public ReadOnlyCollection<ProgramReference> References { get; }
        public SemanticStateTables StateTables { get; }
        public ReadOnlyCollection<ProgramOutputChannelLayout> OutputChannels { get; }
        public ReadOnlyCollection<ProgramCatalogEntry> CatalogEntries { get; }
        public ReadOnlyCollection<ProgramProducer> Producers { get; }
    }

    [ValueStorage]
    public sealed partial class SemanticGraphTables
    {
        internal SemanticGraphTables(SemanticGraphStructure structure, SemanticGraphParameters parameters)
        {
            Structure = structure;
            Parameters = parameters;
        }

        public SemanticGraphStructure Structure { get; }
        public SemanticGraphParameters Parameters { get; }
    }

}
