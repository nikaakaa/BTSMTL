using System;

namespace BTSMTL.Orchestration
{
    public sealed class PreparedOrchestrationGraph<TScalar, TVector2, TVector3, TYaw, TQuaternion>
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
    {
        PreparedOrchestrationGraph(
            OrchestrationGraphContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> source,
            FrozenOrchestrationIdentities identities, ReadOnlyMemory<int> localToShared,
            OrchestrationProgramContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> program,
            ReadOnlyMemory<OrchestrationGraphEntry> entries, ReadOnlyMemory<OrchestrationGraphVariable> variables,
            ReadOnlyMemory<OrchestrationGraphHostInput> hostInputs, ReadOnlyMemory<OrchestrationGraphPort> ports)
        {
            Source = source;
            Identities = identities;
            LocalToShared = localToShared;
            Program = program;
            Entries = entries;
            Variables = variables;
            HostInputs = hostInputs;
            Ports = ports;
        }

        public OrchestrationGraphContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> Source { get; }
        public FrozenOrchestrationIdentities Identities { get; }
        public ReadOnlyMemory<int> LocalToShared { get; }
        public OrchestrationProgramContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> Program { get; }
        public ReadOnlyMemory<OrchestrationGraphEntry> Entries { get; }
        public ReadOnlyMemory<OrchestrationGraphVariable> Variables { get; }
        public ReadOnlyMemory<OrchestrationGraphHostInput> HostInputs { get; }
        public ReadOnlyMemory<OrchestrationGraphPort> Ports { get; }
        public ReadOnlyMemory<OrchestrationGraphSource> Sources => Source.Sources;
        public ReadOnlyMemory<OrchestrationExtensionReference> Extensions => Source.Extensions;

        public static PreparedOrchestrationGraph<TScalar, TVector2, TVector3, TYaw, TQuaternion> Prepare(
            OrchestrationGraphContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> source,
            FrozenOrchestrationIdentities identities, int contentTableIndex,
            ReadOnlySpan<OrchestrationDefaultValue<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>> defaults)
        {
            ReadOnlyMemory<int> mapping = identities.LocalToShared(contentTableIndex);
            OrchestrationProgramContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> data = source.Program;
            var operations = new OrchestrationOperation[data.Operations.Length];
            for (int i = 0; i < operations.Length; i++)
            {
                ref readonly OrchestrationOperation operation = ref data.Operations.Span[i];
                operations[i] = new OrchestrationOperation(operation.Code, operation.IsRunnable,
                    Map(operation.IdentityId, mapping), operation.Integer0, operation.Integer1,
                    operation.ConstantIndex, operation.InputSlot, Map(operation.RequestIdentityId, mapping),
                    operation.ValueSlot, Map(operation.TimelineContentIdentityId, mapping),
                    Map(operation.StateIdentityId, mapping), operation.StateMachineOwnerOperationIndex,
                    operation.ExtensionBindingIndex, operation.GraphCallIndex, operation.Inputs,
                    operation.OutputCount, operation.OutputSlotOffset, operation.LifecycleHooks,
                    operation.BlackboardFactsBindingIndex, operation.ParentStateOperationIndex,
                    operation.OutputAssignments);
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
            foreach (ref readonly OrchestrationDefaultValue<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> value in defaults)
                finalDefaultValues[value.ValueSlot] = value.Value;
            var program = new OrchestrationProgramContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>(
                operations, edges, data.ValueInputs, MapValues(data.Constants, mapping),
                finalDefaultValues, data.GraphCalls, data.Hooks, data.HookInputs,
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
            return new PreparedOrchestrationGraph<TScalar, TVector2, TVector3, TYaw, TQuaternion>(
                source, identities, mapping, program, entries, variables, hostInputs, graphPorts);
        }

        static int Map(int identity, ReadOnlyMemory<int> mapping) =>
            identity == 0 ? 0 : mapping.Span[identity];

        static OrchestrationIdentityValue Map(in OrchestrationIdentityValue identity, ReadOnlyMemory<int> mapping) =>
            identity.Space == OrchestrationIdentitySpace.Content
                ? OrchestrationIdentityValue.Content(Map(identity.Index, mapping)) : identity;

        static OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[] MapValues(
            ReadOnlyMemory<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> values,
            ReadOnlyMemory<int> mapping)
        {
            var result = new OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>[values.Length];
            for (int i = 0; i < result.Length; i++)
                result[i] = MapValue(in values.Span[i], mapping);
            return result;
        }

        internal static OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> MapValue(
            in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value,
            ReadOnlyMemory<int> mapping)
        {
            if (value.Kind == OrchestrationValueKind.Identity)
                return OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromIdentity(
                    Map(value.Identity, mapping));
            if (value.Kind == OrchestrationValueKind.Enum)
                return OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromEnum(
                    new OrchestrationEnumValue(Map(value.Enum.TypeContentId, mapping), value.Enum.Value));
            if (value.Kind == OrchestrationValueKind.SpatialIdentity)
            {
                OrchestrationSpatialIdentity<TVector3, TYaw> spatial = value.SpatialIdentity;
                return OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>.FromSpatialIdentity(
                    new OrchestrationSpatialIdentity<TVector3, TYaw>(Map(spatial.Identity, mapping),
                        spatial.Position, spatial.Yaw));
            }
            return value;
        }
    }
}
