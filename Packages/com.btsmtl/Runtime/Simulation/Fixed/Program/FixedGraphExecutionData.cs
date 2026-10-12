using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation.Fixed
{
    public class FixedGraphExecutionData
    {
        internal GraphExecutionChunks m_Chunks;
        readonly AbilityStateValue[] m_DefaultStateValues;

        protected FixedGraphExecutionData(FixedGraphExecutionData source)
        {
            Tables = source.Tables;
            Operations = source.Operations;
            m_Chunks = source.Chunks;
            GraphSources = source.GraphSources;
            CatalogIndex = source.CatalogIndex;
            Topology = source.Topology;
            m_DefaultStateValues = source.m_DefaultStateValues;
        }

        FixedGraphExecutionData(FixedGraphExecutionData structure, SimulationOperation[] operations,
            ProgramConstant[] constants, ProgramGraphSources sources)
            : this(structure)
        {
            Operations = Array.AsReadOnly(operations);
            Tables = new FixedGraphExecutionTables(structure.Tables.Structure, Array.AsReadOnly(constants), operations[0].ParameterValues);
            GraphSources = sources;
            m_DefaultStateValues = BuildDefaultStateValues(Constants, StateSlots);
            GraphExecutionChunk values = GraphExecutionChunk.Encode(writer =>
                FixedGraphExecutionDataCodec.WriteParameters(writer, this, structure.Chunks.Structure.Hash));
            m_Chunks = new GraphExecutionChunks(structure.Chunks.Structure, values, sources.DiagnosticChunk);
        }

        FixedGraphExecutionData(FixedGraphExecutionData structure, ProgramGraphSources sources)
            : this(structure)
        {
            GraphSources = sources;
            m_Chunks = new GraphExecutionChunks(structure.Chunks.Structure, structure.Chunks.Parameters, sources.DiagnosticChunk);
        }

        internal FixedGraphExecutionData WithSources(ProgramGraphSources sources) =>
            new FixedGraphExecutionData(this, sources);

        internal FixedGraphExecutionData WithParameters(SimulationOperation[] operations,
            ProgramConstant[] constants, ProgramGraphSources sources) =>
            new FixedGraphExecutionData(this, operations, constants, sources);

        public FixedGraphExecutionData(
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
            Tables = new FixedGraphExecutionTables(new FixedGraphExecutionStructure(rootOperation,
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

        internal FixedGraphExecutionTables Tables { get; }
        internal FixedGraphExecutionData(FixedGraphExecutionTables tables, ProgramGraphSources sources)
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

        public GraphExecutionChunks Chunks => m_Chunks ??= FixedGraphExecutionDataCodec.CreateChunks(this);
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
    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    internal sealed partial class FixedGraphExecutionStructure
    {
        internal FixedGraphExecutionStructure(OperationHandle rootOperation,
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

    [CanonicalContract(MaterializedRead = true, PreparedRead = false)]
    internal sealed partial class FixedGraphExecutionTables
    {
        internal FixedGraphExecutionTables(FixedGraphExecutionStructure structure,
            IReadOnlyList<ProgramConstant> constants, SimulationOperationParameterValues[] operationParameters)
        {
            Structure = structure;
            Constants = constants;
            OperationParameters = operationParameters;
        }

        public FixedGraphExecutionStructure Structure { get; }
        public IReadOnlyList<ProgramConstant> Constants { get; }
        [CanonicalPayload] public SimulationOperationParameterValues[] OperationParameters { get; }
    }

}
