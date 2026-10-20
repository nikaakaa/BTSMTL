using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BTSMTL.Orchestration
{
    public sealed class PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>
        where TValue : unmanaged
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TValues : struct, IOrchestrationValueAccess<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion>
    {
        PreparedOrchestrationGraph(
            OrchestrationGraphContent<TValue> source,
            FrozenOrchestrationIdentities identities, ReadOnlyMemory<int> localToShared,
            OrchestrationProgramContent<TValue> program,
            ReadOnlyMemory<OrchestrationGraphEntry> entries, ReadOnlyMemory<OrchestrationGraphVariable> variables,
            ReadOnlyMemory<OrchestrationGraphHostInput> hostInputs, ReadOnlyMemory<OrchestrationGraphPort> ports,
            ReadOnlyMemory<OrchestrationGraphSource> sources)
        {
            Source = source;
            Identities = identities;
            LocalToShared = localToShared;
            Program = program;
            Entries = entries;
            Variables = variables;
            HostInputs = hostInputs;
            Ports = ports;
            Sources = sources;
        }

        public OrchestrationGraphContent<TValue> Source { get; }
        public FrozenOrchestrationIdentities Identities { get; }
        public ReadOnlyMemory<int> LocalToShared { get; }
        public OrchestrationProgramContent<TValue> Program { get; }
        public ReadOnlyMemory<OrchestrationGraphEntry> Entries { get; }
        public ReadOnlyMemory<OrchestrationGraphVariable> Variables { get; }
        public ReadOnlyMemory<OrchestrationGraphHostInput> HostInputs { get; }
        public ReadOnlyMemory<OrchestrationGraphPort> Ports { get; }
        public ReadOnlyMemory<OrchestrationGraphSource> Sources { get; }
        public ReadOnlyMemory<OrchestrationExtensionReference> Extensions => Source.Extensions;

        public static PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues> Prepare(
            OrchestrationGraphContent<TValue> source,
            FrozenOrchestrationIdentities identities, int contentTableIndex,
            ReadOnlySpan<OrchestrationDefaultValue<TValue>> defaults)
        {
            ReadOnlyMemory<int> mapping = identities.LocalToShared(contentTableIndex);
            OrchestrationProgramContent<TValue> data = source.Program;
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
            var finalDefaultValues = MapValues(data.DefaultValues, mapping);
            foreach (ref readonly OrchestrationDefaultValue<TValue> value in defaults)
                finalDefaultValues[value.ValueSlot] = value.Value;
            var program = new OrchestrationProgramContent<TValue>(
                data.Operations, MapOperationPayloads(data, mapping), cold, edges, MapStructure(data.Structure, mapping),
                data.ValueSchedule, data.WorkspaceCapacity, data.ExecutionStorage, data.ValueInputs, MapValues(data.Constants, mapping),
                finalDefaultValues, data.ValueSlotTypes, data.ValueKindSlotCounts, data.GraphCalls, data.Hooks, data.HookInputs,
                data.BlackboardSlots, data.BlackboardOutputs, data.OutputTemplates, mappedPorts,
                data.RootOperationIndex, data.MaximumOperationEvaluations);
            var entries = new OrchestrationGraphEntry[source.Entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                ref readonly OrchestrationGraphEntry entry = ref source.Entries.Span[i];
                OrchestrationExecutionEntry execution = entry.Execution;
                entries[i] = new OrchestrationGraphEntry(Map(entry.EntryIdentityId, mapping),
                    new OrchestrationExecutionEntry(execution.OperationIndex, execution.Contract,
                        Map(execution.InputPortId, mapping)));
            }
            var variables = new OrchestrationGraphVariable[source.Variables.Length];
            for (int i = 0; i < variables.Length; i++)
            {
                ref readonly OrchestrationGraphVariable variable = ref source.Variables.Span[i];
                variables[i] = new OrchestrationGraphVariable(Map(variable.GraphIdentityId, mapping),
                    Map(variable.DeclarationIdentityId, mapping), Map(variable.KeyIdentityId, mapping),
                    Map(variable.ValueTypeIdentityId, mapping), variable.IsPublic, variable.ValueSlot);
            }
            var hostInputs = new OrchestrationGraphHostInput[source.HostInputs.Length];
            for (int i = 0; i < hostInputs.Length; i++)
            {
                ref readonly OrchestrationGraphHostInput input = ref source.HostInputs.Span[i];
                hostInputs[i] = new OrchestrationGraphHostInput(Map(input.InputIdentityId, mapping),
                    Map(input.ValueTypeIdentityId, mapping), input.InputSlot);
            }
            var graphPorts = new OrchestrationGraphPort[source.Ports.Length];
            for (int i = 0; i < graphPorts.Length; i++)
            {
                ref readonly OrchestrationGraphPort port = ref source.Ports.Span[i];
                graphPorts[i] = new OrchestrationGraphPort(Map(port.GraphIdentityId, mapping),
                    Map(port.NodeIdentityId, mapping), Map(port.PortIdentityId, mapping), port.OperationIndex,
                    port.IsInput, port.IsTrigger, Map(port.CompiledPortIdentityId, mapping), port.ValueIndex);
            }
            var sources = new OrchestrationGraphSource[source.Sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                OrchestrationGraphSource item = source.Sources.Span[i];
                OrchestrationGraphExecutionSource execution = item.Source;
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
            return new PreparedOrchestrationGraph<TValue, TScalar, TVector2, TVector3, TYaw, TQuaternion, TValues>(
                source, identities, mapping, program, entries, variables, hostInputs, graphPorts, sources);
        }

        // 值候选只替换常量与默认值；其余内容按正式编码逐字节核对，结构、扩展参数或身份表变化均拒绝。
        public OrchestrationProgramContent<TValue> PrepareValueCandidate(
            OrchestrationGraphContent<TValue> candidate,
            Func<OrchestrationGraphContent<TValue>, byte[]> encode,
            out string rejection)
        {
            OrchestrationProgramContent<TValue> original = Source.Program;
            OrchestrationProgramContent<TValue> edited = candidate.Program;
            rejection = candidate.Identity.GraphId != Source.Identity.GraphId ? "图身份已变化"
                : edited.Constants.Length != original.Constants.Length ||
                  edited.DefaultValues.Length != original.DefaultValues.Length ? "常量或值槽数量已变化"
                : null;
            if (rejection != null)
                return null;
            var probe = new OrchestrationGraphContent<TValue>(
                Source.Identity, candidate.NumericProfile, candidate.Identities,
                new OrchestrationProgramContent<TValue>(
                    edited.Operations, edited.OperationPayloads, edited.ColdOperations, edited.Edges, edited.Structure,
                    edited.ValueSchedule, edited.WorkspaceCapacity, edited.ExecutionStorage, edited.ValueInputs, original.Constants, original.DefaultValues,
                    edited.ValueSlotTypes, edited.ValueKindSlotCounts,
                    edited.GraphCalls, edited.Hooks, edited.HookInputs, edited.BlackboardSlots,
                    edited.BlackboardOutputs, edited.OutputTemplates, edited.CorePorts,
                    edited.RootOperationIndex, edited.MaximumOperationEvaluations),
                candidate.Extensions, candidate.OperationContents, candidate.Entries, candidate.Variables,
                candidate.GeneratedBlackboards, candidate.HostInputs, candidate.Ports, candidate.Sources);
            if (!encode(probe).AsSpan().SequenceEqual(encode(Source)))
            {
                rejection = "节点结构、连线、扩展参数或身份表已变化";
                return null;
            }
            var comparer = System.Collections.Generic.EqualityComparer<TValue>.Default;
            var defaults = Program.DefaultValues.ToArray();
            for (int i = 0; i < defaults.Length; i++)
                if (comparer.Equals(defaults[i], MapValue(in original.DefaultValues.Span[i], LocalToShared)))
                    defaults[i] = MapValue(in edited.DefaultValues.Span[i], LocalToShared);
            OrchestrationProgramContent<TValue> prepared = Program;
            return new OrchestrationProgramContent<TValue>(
                prepared.Operations, prepared.OperationPayloads, prepared.ColdOperations, prepared.Edges, prepared.Structure,
                prepared.ValueSchedule, prepared.WorkspaceCapacity, prepared.ExecutionStorage, prepared.ValueInputs, MapValues(edited.Constants, LocalToShared),
                defaults, prepared.ValueSlotTypes, prepared.ValueKindSlotCounts,
                prepared.GraphCalls, prepared.Hooks, prepared.HookInputs, prepared.BlackboardSlots,
                prepared.BlackboardOutputs, prepared.OutputTemplates, prepared.CorePorts,
                prepared.RootOperationIndex, prepared.MaximumOperationEvaluations);
        }

        static byte[] MapOperationPayloads(OrchestrationProgramContent<TValue> source, ReadOnlyMemory<int> mapping)
        {
            byte[] payloads = source.OperationPayloads.ToArray();
            for (int i = 0; i < source.Operations.Length; i++)
            {
                ref readonly OrchestrationOperation operation = ref source.Operations.Span[i];
                switch (operation.Code)
                {
                    case OrchestrationOperationCode.State:
                    {
                        OrchestrationStatePayload state = source.ReadOperationPayload<OrchestrationStatePayload>(i);
                        Write(new OrchestrationStatePayload(Map(state.Identity.IdentityId, mapping), state.MachineIndex), operation.PayloadOffset);
                        break;
                    }
                    case OrchestrationOperationCode.StateExit:
                        Write(new OrchestrationStateIdentityPayload(Map(source.ReadOperationPayload<OrchestrationStateIdentityPayload>(i).IdentityId, mapping)), operation.PayloadOffset);
                        break;
                    case OrchestrationOperationCode.InputRequest:
                        Write(new OrchestrationRequestPayload(Map(source.ReadOperationPayload<OrchestrationRequestPayload>(i).IdentityId, mapping)), operation.PayloadOffset);
                        break;
                    case OrchestrationOperationCode.Timeline:
                    {
                        OrchestrationTimelinePayload timeline = source.ReadOperationPayload<OrchestrationTimelinePayload>(i);
                        Write(new OrchestrationTimelinePayload(Map(timeline.ContentIdentityId, mapping), timeline.Mode), operation.PayloadOffset);
                        break;
                    }
                    case OrchestrationOperationCode.EmitOutput:
                    {
                        OrchestrationOutputPayload output = source.ReadOperationPayload<OrchestrationOutputPayload>(i);
                        Write(new OrchestrationOutputPayload(output.TemplateIndex, Map(output.ProducerIdentityId, mapping)), operation.PayloadOffset);
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

        internal static TValue MapValue(
            in TValue value,
            ReadOnlyMemory<int> mapping)
        {
            TValues values = default;
            OrchestrationValueKind kind = values.Kind(in value);
            if (kind == OrchestrationValueKind.Identity)
                return values.FromIdentity(Map(values.Identity(in value), mapping));
            if (kind == OrchestrationValueKind.Enum)
            {
                OrchestrationEnumValue enumValue = values.Enum(in value);
                return values.FromEnum(new OrchestrationEnumValue(Map(enumValue.TypeContentId, mapping), enumValue.Value));
            }
            if (kind == OrchestrationValueKind.SpatialIdentity)
            {
                OrchestrationSpatialIdentity<TVector3, TYaw> spatial = values.SpatialIdentity(in value);
                return values.FromSpatialIdentity(
                    new OrchestrationSpatialIdentity<TVector3, TYaw>(Map(spatial.Identity, mapping),
                        spatial.Position, spatial.Yaw));
            }
            return value;
        }
    }
}
