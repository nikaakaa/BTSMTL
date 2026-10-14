using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using SimulationOperationParameterValues = ThirdPersonSimulation.SimulationOperationParameterValues<Photon.Deterministic.FP>;

namespace ThirdPersonSimulation.FPGameplay
{
    public class FPGraphExecutionData
    {
        internal GraphExecutionChunks m_Chunks;
        readonly AbilityStateValue[] m_DefaultStateValues;

        protected FPGraphExecutionData(FPGraphExecutionData source)
        {
            Tables = source.Tables;
            Operations = source.Operations;
            m_Chunks = source.Chunks;
            GraphSources = source.GraphSources;
            CatalogIndex = source.CatalogIndex;
            Topology = source.Topology;
            m_DefaultStateValues = source.m_DefaultStateValues;
        }

        FPGraphExecutionData(FPGraphExecutionData structure, SimulationOperation[] operations,
            ProgramConstant[] constants, ProgramGraphSources sources)
            : this(structure)
        {
            Operations = Array.AsReadOnly(operations);
            Tables = new FPGraphExecutionTables(structure.Tables.Structure, Array.AsReadOnly(constants), operations[0].ParameterValues);
            GraphSources = sources;
            m_DefaultStateValues = BuildDefaultStateValues(Constants, StateSlots);
            m_Chunks = sources.Layout.Publisher.Publish(Tables, sources, structure.Chunks, GraphCompilationWork.Parameters);
        }

        FPGraphExecutionData(FPGraphExecutionData structure, ProgramGraphSources sources)
            : this(structure)
        {
            GraphSources = sources;
            m_Chunks = sources.Layout.Publisher.Publish(Tables, sources, structure.Chunks, GraphCompilationWork.Sources);
        }

        internal FPGraphExecutionData WithSources(ProgramGraphSources sources) =>
            new FPGraphExecutionData(this, sources);

        internal FPGraphExecutionData WithParameters(SimulationOperation[] operations,
            ProgramConstant[] constants, ProgramGraphSources sources) =>
            new FPGraphExecutionData(this, operations, constants, sources);

        public FPGraphExecutionData(
            OperationHandle rootOperation,
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
            ProgramGraphSources graphSources,
            IEnumerable<ProgramProducer> producers)
        {
            Operations = Copy(operations);
            Tables = new FPGraphExecutionTables(new FPGraphExecutionStructure(rootOperation,
                operationDefinitions, Operations, constantInputBindings, controlFlow, references, graphCallFrames,
                stateSlots, scopes, outputChannels, catalogEntries, graphSources.Layout.Chunk, producers),
                Copy(constants), Operations[0].ParameterValues);
            GraphSources = graphSources;
            CatalogIndex = new ProgramCatalogRuntimeIndex(Operations.Count, References, CatalogEntries);
            Topology = new OperationExecutionTopology(
                BuildOperationDescriptors(Operations),
                ControlFlow,
                References,
                StateSlots,
                ExecutionSources,
                rootOperation,
                GraphCallFrames);
            m_DefaultStateValues = BuildDefaultStateValues(Constants, StateSlots);
        }

        public FPGraphExecutionTables Tables { get; }
        public FPGraphExecutionData(FPGraphExecutionTables tables, ProgramGraphSources sources)
        {
            Tables = tables;
            GraphSources = sources;
            var operations = new SimulationOperation[tables.Structure.OperationLayouts.Count];
            for (int i = 0; i < operations.Length; i++)
            {
                ProgramOperationLayout layout = tables.Structure.OperationLayouts[i];
                if (layout.ParameterIndex < 0 || layout.ParameterIndex >= tables.OperationParameters.Length)
                    throw new System.IO.InvalidDataException($"Operation '{layout.Handle}' parameter index '{layout.ParameterIndex}' is invalid.");
                if (layout.DefinitionIndex < 0 || layout.DefinitionIndex >= tables.Structure.OperationDefinitions.Count)
                    throw new System.IO.InvalidDataException($"Operation '{layout.Handle}' definition index '{layout.DefinitionIndex}' is invalid.");
                operations[i] = new SimulationOperation(layout, tables.Structure.OperationDefinitions[layout.DefinitionIndex], tables.OperationParameters);
            }
            Operations = Array.AsReadOnly(operations);
            CatalogIndex = new ProgramCatalogRuntimeIndex(Operations.Count, References, CatalogEntries);
            Topology = new OperationExecutionTopology(BuildOperationDescriptors(Operations), ControlFlow, References,
                StateSlots, ExecutionSources, tables.Structure.RootOperation, GraphCallFrames);
            m_DefaultStateValues = BuildDefaultStateValues(Constants, StateSlots);
        }

        public GraphExecutionChunks Chunks => m_Chunks ??= GraphSources.Layout.Publisher.Publish(Tables, GraphSources, null, GraphCompilationWork.Structure);
        public IReadOnlyList<SimulationOperationDefinition> OperationDefinitions => Tables.Structure.OperationDefinitions;
        public IReadOnlyList<SimulationOperation> Operations { get; }
        public IReadOnlyList<ProgramConstant> Constants => Tables.Constants;
        public IReadOnlyList<ProgramConstantInputBinding> ConstantInputBindings => Tables.Structure.ConstantInputBindings;
        public IReadOnlyList<ProgramControlFlowEdge> ControlFlow => Tables.Structure.ControlFlow;
        public IReadOnlyList<ProgramReference> References => Tables.Structure.References;
        public IReadOnlyList<ProgramGraphCallFrame> GraphCallFrames => Tables.Structure.GraphCallFrames;
        public IReadOnlyList<ProgramStateSlot> StateSlots => Tables.Structure.StateSlots;
        public IReadOnlyList<ProgramScopeLayout> Scopes => Tables.Structure.Scopes;
        public IReadOnlyList<ProgramOutputChannelLayout> OutputChannels => Tables.Structure.OutputChannels;
        public AbilityStateValue DefaultStateValue(int slotIndex) => m_DefaultStateValues[slotIndex];
        public IReadOnlyList<ProgramCatalogEntry> CatalogEntries => Tables.Structure.CatalogEntries;
        public ProgramGraphSources GraphSources { get; }
        public IReadOnlyList<ProgramSourceMapEntry> SourceMap => GraphSources.Diagnostics;
        public ProgramExecutionSourceLayout ExecutionSourceLayout => GraphSources.Layout;
        public IReadOnlyList<ProgramExecutionSource> ExecutionSources => GraphSources.Layout.Entries;
        public ProgramGraphSourceRevisionTable GraphSourceRevisions => GraphSources.Revisions;
        public IReadOnlyList<ProgramProducer> Producers => Tables.Structure.Producers;
        public ProgramCatalogRuntimeIndex CatalogIndex { get; }
        public OperationExecutionTopology Topology { get; }
        public OperationHandle RootOperation => Topology.RootOperation;


        static AbilityStateValue[] BuildDefaultStateValues(IReadOnlyList<ProgramConstant> constants,
            IReadOnlyList<ProgramStateSlot> stateSlots)
        {
            var values = new AbilityStateValue[stateSlots.Count];
            for (int i = 0; i < values.Length; i++)
            {
                ProgramStateSlot slot = stateSlots[i];
                values[i] = slot.DefaultConstantIndex >= 0
                    ? AbilityStateValue.FromConstant(constants[slot.DefaultConstantIndex], slot.ValueKind)
                    : AbilityStateValue.Default(slot.ValueKind);
            }
            return values;
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

        static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) =>
            new ReadOnlyCollection<T>(new List<T>(values ?? Array.Empty<T>()));
    }
    [ValueStorage]
    public sealed partial class FPGraphExecutionStructure
    {
        internal FPGraphExecutionStructure(OperationHandle rootOperation,
            IEnumerable<SimulationOperationDefinition> operationDefinitions, IReadOnlyList<SimulationOperation> operations,
            IEnumerable<ProgramConstantInputBinding> constantInputBindings, IEnumerable<ProgramControlFlowEdge> controlFlow,
            IEnumerable<ProgramReference> references, IEnumerable<ProgramGraphCallFrame> graphCallFrames,
            IEnumerable<ProgramStateSlot> stateSlots, IEnumerable<ProgramScopeLayout> scopes,
            IEnumerable<ProgramOutputChannelLayout> outputChannels, IEnumerable<ProgramCatalogEntry> catalogEntries,
            GraphExecutionChunk executionSources, IEnumerable<ProgramProducer> producers)
        {
            RootOperation = rootOperation;
            OperationDefinitions = Copy(operationDefinitions);
            var layouts = new ProgramOperationLayout[operations.Count];
            for (int i = 0; i < layouts.Length; i++)
                layouts[i] = operations[i].Layout;
            OperationLayouts = Array.AsReadOnly(layouts);
            ConstantInputBindings = Copy(constantInputBindings);
            ControlFlow = Copy(controlFlow);
            References = Copy(references);
            GraphCallFrames = Copy(graphCallFrames);
            StateSlots = Copy(stateSlots);
            Scopes = Copy(scopes);
            OutputChannels = Copy(outputChannels);
            CatalogEntries = Copy(catalogEntries);
            ExecutionSources = executionSources;
            Producers = Copy(producers);
        }

        public OperationHandle RootOperation { get; }
        public GraphExecutionChunk ExecutionSources { get; }
        public IReadOnlyList<SimulationOperationDefinition> OperationDefinitions { get; }
        public IReadOnlyList<ProgramOperationLayout> OperationLayouts { get; }
        public IReadOnlyList<ProgramConstantInputBinding> ConstantInputBindings { get; }
        public IReadOnlyList<ProgramControlFlowEdge> ControlFlow { get; }
        public IReadOnlyList<ProgramReference> References { get; }
        public IReadOnlyList<ProgramGraphCallFrame> GraphCallFrames { get; }
        public IReadOnlyList<ProgramStateSlot> StateSlots { get; }
        public IReadOnlyList<ProgramScopeLayout> Scopes { get; }
        public IReadOnlyList<ProgramOutputChannelLayout> OutputChannels { get; }
        public IReadOnlyList<ProgramCatalogEntry> CatalogEntries { get; }
        public IReadOnlyList<ProgramProducer> Producers { get; }

        static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values) =>
            new List<T>(values ?? Array.Empty<T>()).AsReadOnly();
    }

    [ValueStorage]
    public sealed partial class FPGraphExecutionTables
    {
        internal FPGraphExecutionTables(FPGraphExecutionStructure structure,
            IReadOnlyList<ProgramConstant> constants, SimulationOperationParameterValues[] operationParameters)
        {
            Structure = structure;
            Constants = constants;
            OperationParameters = operationParameters;
        }

        public FPGraphExecutionStructure Structure { get; }
        public IReadOnlyList<ProgramConstant> Constants { get; }
        [ValuePayload] public SimulationOperationParameterValues[] OperationParameters { get; }
    }

}
