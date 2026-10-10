using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace ThirdPersonSimulation
{
    public class Float32GraphExecutionData
    {
        readonly AbilityStateValue[] m_DefaultStateValues;

        protected Float32GraphExecutionData(Float32GraphExecutionData source)
        {
            OperationDefinitions = source.OperationDefinitions;
            Operations = source.Operations;
            Constants = source.Constants;
            ConstantInputBindings = source.ConstantInputBindings;
            ControlFlow = source.ControlFlow;
            References = source.References;
            GraphCallFrames = source.GraphCallFrames;
            StateSlots = source.StateSlots;
            Scopes = source.Scopes;
            OutputChannels = source.OutputChannels;
            CatalogEntries = source.CatalogEntries;
            SourceMap = source.SourceMap;
            Producers = source.Producers;
            CatalogIndex = source.CatalogIndex;
            Topology = source.Topology;
            ValueLayout = source.ValueLayout;
            m_DefaultStateValues = source.m_DefaultStateValues;
        }

        public Float32GraphExecutionData(
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
            IEnumerable<ProgramSourceMapEntry> sourceMap,
            IEnumerable<ProgramProducer> producers)
        {
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
                rootOperation,
                GraphCallFrames);
            ValueLayout = new Float32GraphValueLayout(this);
            m_DefaultStateValues = new AbilityStateValue[StateSlots.Count];
            for (int index = 0; index < m_DefaultStateValues.Length; index++)
            {
                ProgramStateSlot slot = StateSlots[index];
                m_DefaultStateValues[index] = slot.DefaultConstantIndex >= 0
                    ? AbilityStateValue.FromConstant(Constants[slot.DefaultConstantIndex], slot.ValueKind)
                    : AbilityStateValue.Default(slot.ValueKind);
            }
        }

        public Float32GraphValueLayout ValueLayout { get; }
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
}
