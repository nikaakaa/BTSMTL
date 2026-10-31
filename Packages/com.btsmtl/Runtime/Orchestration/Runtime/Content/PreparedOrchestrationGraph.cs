using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BTSMTL.Orchestration
{
    public sealed class PreparedOrchestrationGraph<TValue, TNumerics>
        where TValue : unmanaged
        where TNumerics : struct, IOrchestrationNumerics<TValue>
    {
        PreparedOrchestrationGraph(
            OrchestrationGraphContent<TValue> source,
            FrozenOrchestrationIdentities identities, ReadOnlyMemory<int> localToShared,
            OrchestrationProgramContent program, OrchestrationGraphParameters<TValue> parameters,
            ReadOnlyMemory<OrchestrationGraphEntry> entries, ReadOnlyMemory<OrchestrationGraphVariable> variables,
            ReadOnlyMemory<OrchestrationGraphHostInput> hostInputs, ReadOnlyMemory<OrchestrationGraphPort> ports,
            ReadOnlyMemory<OrchestrationGraphSource> sources)
        {
            Source = source;
            Identities = identities;
            LocalToShared = localToShared;
            Program = program;
            Parameters = parameters;
            Entries = entries;
            Variables = variables;
            HostInputs = hostInputs;
            Ports = ports;
            Sources = sources;
        }

        public OrchestrationGraphContent<TValue> Source { get; }
        public FrozenOrchestrationIdentities Identities { get; }
        public ReadOnlyMemory<int> LocalToShared { get; }
        public OrchestrationProgramContent Program { get; }
        public OrchestrationGraphParameters<TValue> Parameters { get; }
        public ReadOnlyMemory<OrchestrationGraphEntry> Entries { get; }
        public ReadOnlyMemory<OrchestrationGraphVariable> Variables { get; }
        public ReadOnlyMemory<OrchestrationGraphHostInput> HostInputs { get; }
        public ReadOnlyMemory<OrchestrationGraphPort> Ports { get; }
        public ReadOnlyMemory<OrchestrationGraphSource> Sources { get; }
        public ReadOnlyMemory<OrchestrationExtensionReference> Extensions => Source.Structure.Extensions;

        public static PreparedOrchestrationGraph<TValue, TNumerics> Prepare(
            OrchestrationGraphContent<TValue> source,
            FrozenOrchestrationIdentities identities, int contentTableIndex,
            ReadOnlySpan<OrchestrationDefaultValue<TValue>> defaults)
        {
            ReadOnlyMemory<int> mapping = identities.LocalToShared(contentTableIndex);
            OrchestrationGraphStructure structure = source.Structure;
            OrchestrationProgramContent data = structure.Program;
            OrchestrationGraphParameters<TValue> values = source.Parameters;
            var cold = new OrchestrationOperationColdData[data.ColdOperations.Length];
            for (int i = 0; i < cold.Length; i++)
            {
                ref readonly OrchestrationOperationColdData sourceCold = ref data.ColdOperations.Span[i];
                cold[i] = new OrchestrationOperationColdData(Map(sourceCold.IdentityId, mapping), sourceCold.ValueVariant);
            }
            var edges = new OrchestrationFlowEdge[data.Edges.Length];
            for (int i = 0; i < edges.Length; i++)
            {
                ref readonly OrchestrationFlowEdge edge = ref data.Edges.Span[i];
                edges[i] = new OrchestrationFlowEdge(Map(edge.IdentityId, mapping),
                    edge.SourceOperationIndex, edge.TargetOperationIndex, Map(edge.SourcePortId, mapping),
                    Map(edge.TargetPortId, mapping), edge.Kind, edge.Order, edge.Priority,
                    edge.AbortPolicy, edge.ConditionOperationIndex);
            }
            OrchestrationCorePorts ports = data.CorePorts;
            var mappedPorts = new OrchestrationCorePorts(Map(ports.AnyState, mapping),
                Map(ports.StateOnEnter, mapping), Map(ports.StateRoot, mapping), Map(ports.StateOnExit, mapping),
                Map(ports.BranchTrue, mapping), Map(ports.BranchFalse, mapping),
                Map(ports.BranchThen, mapping), Map(ports.CycleReset, mapping));
            var finalDefaultValues = MapValues(values.DefaultValues, mapping);
            foreach (ref readonly OrchestrationDefaultValue<TValue> value in defaults)
                finalDefaultValues[value.ValueSlot] = value.Value;
            var program = new OrchestrationProgramContent(
                data.Operations, cold, edges, MapStructure(data.Structure, mapping),
                data.ValueSchedule, data.WorkspaceCapacity, data.ExecutionStorage, data.ValueInputs,
                data.ValueSlotTypes, data.ValueKindSlotCounts, data.GraphCalls, data.Hooks, data.HookInputs,
                data.BlackboardSlots, data.BlackboardOutputs, data.OutputTemplates, mappedPorts,
                data.RootOperationIndex, data.MaximumOperationEvaluations);
            var parameters = new OrchestrationGraphParameters<TValue>(values.SourceIdentity, values.Identities,
                MapOperationPayloads(data, values, mapping), MapValues(values.Constants, mapping),
                finalDefaultValues, values.OperationContents);
            var entries = new OrchestrationGraphEntry[structure.Entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                ref readonly OrchestrationGraphEntry entry = ref structure.Entries.Span[i];
                OrchestrationExecutionEntry execution = entry.Execution;
                entries[i] = new OrchestrationGraphEntry(Map(entry.EntryIdentityId, mapping),
                    new OrchestrationExecutionEntry(execution.OperationIndex, execution.Contract,
                        Map(execution.InputPortId, mapping)));
            }
            var variables = new OrchestrationGraphVariable[structure.Variables.Length];
            for (int i = 0; i < variables.Length; i++)
            {
                ref readonly OrchestrationGraphVariable variable = ref structure.Variables.Span[i];
                variables[i] = new OrchestrationGraphVariable(Map(variable.GraphIdentityId, mapping),
                    Map(variable.DeclarationIdentityId, mapping), Map(variable.KeyIdentityId, mapping),
                    Map(variable.ValueTypeIdentityId, mapping), variable.IsPublic, variable.ValueSlot);
            }
            var hostInputs = new OrchestrationGraphHostInput[structure.HostInputs.Length];
            for (int i = 0; i < hostInputs.Length; i++)
            {
                ref readonly OrchestrationGraphHostInput input = ref structure.HostInputs.Span[i];
                hostInputs[i] = new OrchestrationGraphHostInput(Map(input.InputIdentityId, mapping),
                    Map(input.ValueTypeIdentityId, mapping), input.InputSlot);
            }
            var graphPorts = new OrchestrationGraphPort[structure.Ports.Length];
            for (int i = 0; i < graphPorts.Length; i++)
            {
                ref readonly OrchestrationGraphPort port = ref structure.Ports.Span[i];
                graphPorts[i] = new OrchestrationGraphPort(Map(port.GraphIdentityId, mapping),
                    Map(port.NodeIdentityId, mapping), Map(port.PortIdentityId, mapping), port.OperationIndex,
                    port.IsInput, port.IsTrigger, Map(port.CompiledPortIdentityId, mapping), port.ValueIndex);
            }
            var sources = new OrchestrationGraphSource[structure.Sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                OrchestrationGraphSourceText item = source.Sources.Entries.Span[i];
                OrchestrationGraphExecutionSource execution = structure.Sources.Span[i];
                if ((execution.TargetKind == OrchestrationGraphSourceTargetKind.Operation ||
                     execution.TargetKind == OrchestrationGraphSourceTargetKind.GraphInvocation) &&
                    execution.SourceIdentityId <= OrchestrationGraphExecutionSource.NoExecutionIdentity)
                    throw new InvalidDataException($"执行来源 '{execution.TargetKind}:{execution.TargetIndex}' 缺少已发布的发生点身份编号。");
                sources[i] = new OrchestrationGraphSource(new OrchestrationGraphExecutionSource(
                    execution.TargetKind, execution.TargetIndex, execution.GraphId, execution.NodeId,
                    execution.PortId, execution.EdgeId, execution.DeclarationId, execution.TimelineId,
                    execution.TrackId, execution.ClipId, execution.IsInputPort, execution.GraphInvocationPath,
                    execution.CompiledPortId, execution.ValuePortDirection, execution.SourceInvocationPath,
                    execution.ParentInvocationPath, execution.InvocationCallerKind, execution.InvocationCallerId,
                    execution.InvocationCallerClipId, execution.InvocationCallerOperationIndex,
                    execution.InvocationEntry, Map(execution.SourceIdentityId, mapping)),
                    item.SourceType, item.DisplayPathSegments);
            }
            return new PreparedOrchestrationGraph<TValue, TNumerics>(
                source, identities, mapping, program, parameters, entries, variables, hostInputs, graphPorts, sources);
        }

        public PreparedOrchestrationGraph<TValue, TNumerics> PrepareParameterCandidate(
            OrchestrationGraphContent<TValue> candidate,
            out string rejection)
        {
            OrchestrationGraphParameters<TValue> original = Source.Parameters;
            OrchestrationGraphParameters<TValue> edited = candidate.Parameters;
            rejection = candidate.Identity.StructureIdentity != Source.Identity.StructureIdentity ? "节点结构、接口或参数布局已变化"
                : candidate.Identity.StateLayoutIdentity != Source.Identity.StateLayoutIdentity ? "状态布局已变化"
                : candidate.Identities.Identity != Source.Identities.Identity ? "参数需要新的身份绑定"
                : null;
            if (rejection != null)
                return null;
            var comparer = System.Collections.Generic.EqualityComparer<TValue>.Default;
            var defaults = Parameters.DefaultValues.ToArray();
            for (int i = 0; i < defaults.Length; i++)
                if (comparer.Equals(defaults[i], MapValue(in original.DefaultValues.Span[i], LocalToShared)))
                    defaults[i] = MapValue(in edited.DefaultValues.Span[i], LocalToShared);
            var parameters = new OrchestrationGraphParameters<TValue>(edited.SourceIdentity, edited.Identities,
                MapOperationPayloads(candidate.Structure.Program, edited, LocalToShared),
                MapValues(edited.Constants, LocalToShared), defaults, edited.OperationContents);
            ReadOnlyMemory<OrchestrationGraphSource> sources = Sources;
            if (!ReferenceEquals(candidate.Sources, Source.Sources))
            {
                var mapped = new OrchestrationGraphSource[Sources.Length];
                for (int i = 0; i < mapped.Length; i++)
                {
                    OrchestrationGraphSourceText text = candidate.Sources.Entries.Span[i];
                    mapped[i] = new OrchestrationGraphSource(Sources.Span[i].Source, text.SourceType, text.DisplayPathSegments);
                }
                sources = mapped;
            }
            return new PreparedOrchestrationGraph<TValue, TNumerics>(
                candidate, Identities, LocalToShared, Program, parameters, Entries, Variables, HostInputs, Ports, sources);
        }

        static byte[] MapOperationPayloads(OrchestrationProgramContent source,
            OrchestrationGraphParameters<TValue> parameters, ReadOnlyMemory<int> mapping)
        {
            byte[] payloads = parameters.OperationPayloads.ToArray();
            for (int i = 0; i < source.Operations.Length; i++)
            {
                ref readonly OrchestrationOperation operation = ref source.Operations.Span[i];
                switch (operation.Code)
                {
                    case OrchestrationOperationCode.State:
                    {
                        OrchestrationStatePayload state = parameters.ReadOperationPayload<OrchestrationStatePayload>(source, i);
                        Write(new OrchestrationStatePayload(Map(state.Identity.IdentityId, mapping), state.MachineIndex), operation.ParameterOffset);
                        break;
                    }
                    case OrchestrationOperationCode.StateExit:
                        Write(new OrchestrationStateIdentityPayload(Map(parameters.ReadOperationPayload<OrchestrationStateIdentityPayload>(source, i).IdentityId, mapping)), operation.ParameterOffset);
                        break;
                    case OrchestrationOperationCode.InputRequest:
                        Write(new OrchestrationRequestPayload(Map(parameters.ReadOperationPayload<OrchestrationRequestPayload>(source, i).IdentityId, mapping)), operation.ParameterOffset);
                        break;
                    case OrchestrationOperationCode.Timeline:
                    {
                        OrchestrationTimelinePayload timeline = parameters.ReadOperationPayload<OrchestrationTimelinePayload>(source, i);
                        Write(new OrchestrationTimelinePayload(Map(timeline.ContentIdentityId, mapping), timeline.Mode), operation.ParameterOffset);
                        break;
                    }
                    case OrchestrationOperationCode.EmitOutput:
                    {
                        OrchestrationOutputPayload output = parameters.ReadOperationPayload<OrchestrationOutputPayload>(source, i);
                        Write(new OrchestrationOutputPayload(output.TemplateIndex, Map(output.ProducerIdentityId, mapping)), operation.ParameterOffset);
                        break;
                    }
                }
            }
            return payloads;

            void Write<T>(T value, int offset) where T : unmanaged =>
                MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref value, 1)).CopyTo(payloads.AsSpan(offset));
        }

        static OrchestrationProgramStructure MapStructure(OrchestrationProgramStructure source,
            ReadOnlyMemory<int> mapping)
        {
            ReadOnlySpan<int> local = source.StateOperationIndexes.Span;
            int maximumIdentity = 0;
            for (int i = 1; i < local.Length; i++)
                if (local[i] >= 0)
                    maximumIdentity = Math.Max(maximumIdentity, Map(i, mapping));
            var states = new int[maximumIdentity + 1];
            Array.Fill(states, -1);
            for (int i = 1; i < local.Length; i++)
                if (local[i] >= 0)
                    states[Map(i, mapping)] = local[i];
            return new OrchestrationProgramStructure(source.OutgoingEdgeIndexes, source.OutgoingOffsets,
                source.InitialStateEntries, source.AnyStateEntries, source.StateOnEnterEdges,
                source.StateRootEdges, source.StateOnExitEdges, source.ValueEvaluationOffsets, states);
        }

        static int Map(int identity, ReadOnlyMemory<int> mapping) =>
            identity == 0 ? 0 : mapping.Span[identity];

        static OrchestrationIdentityValue Map(in OrchestrationIdentityValue identity, ReadOnlyMemory<int> mapping) =>
            identity.Space == OrchestrationIdentitySpace.Content
                ? OrchestrationIdentityValue.Content(Map(identity.Index, mapping)) : identity;

        static TValue[] MapValues(
            ReadOnlyMemory<TValue> values,
            ReadOnlyMemory<int> mapping)
        {
            var result = new TValue[values.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = MapValue(in values.Span[i], mapping);
            return result;
        }

        internal static TValue MapValue(in TValue value, ReadOnlyMemory<int> mapping) =>
            default(TNumerics).MapContentIdentities(in value, mapping.Span);
    }
}
