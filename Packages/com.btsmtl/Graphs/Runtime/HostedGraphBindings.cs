using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs
{
    sealed class HostedGraphBindings<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric>
        where TScalar : unmanaged
        where TVector2 : unmanaged
        where TVector3 : unmanaged
        where TYaw : unmanaged
        where TQuaternion : unmanaged
        where TNumeric : struct, IGraphNumericAdapter<TScalar, TVector2, TVector3, TQuaternion>
    {
        internal readonly OrchestrationGraphContent<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> Content;
        internal readonly PreparedOrchestrationGraph<TScalar, TVector2, TVector3, TYaw, TQuaternion> Prepared;
        internal readonly OrchestrationProgram<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> Program;
        internal readonly GraphVariableContract Variables;
        internal readonly Type[] StateTypes;
        internal readonly int[] PublishedSlots;
        internal readonly string[] OperationNodes;
        internal readonly OrchestrationExecutionEntry Start;
        internal readonly OrchestrationExecutionEntry Update;
        internal readonly InputBinding[] Inputs;
        internal readonly int InputCapacity;
        internal readonly int SourceIdentityId;

        internal readonly struct InputBinding
        {
            internal InputBinding(int hostSlot, int inputSlot, int enumTypeContentId,
                GraphHostInputDescriptor descriptor)
            {
                HostSlot = hostSlot;
                InputSlot = inputSlot;
                EnumTypeContentId = enumTypeContentId;
                ValueKind = descriptor.ValueKind;
                ValueType = descriptor.ValueType;
                InputId = descriptor.InputId;
            }

            internal int HostSlot { get; }
            internal int InputSlot { get; }
            internal int EnumTypeContentId { get; }
            internal GraphHostValueKind ValueKind { get; }
            internal Type ValueType { get; }
            internal string InputId { get; }
        }

        internal HostedGraphBindings(BtsmtlGraphRoot source, GraphHostContract contract,
            IOrchestrationGraphContentEncoding<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> encoding)
        {
            Content = source.RequireExecutionData(encoding, contract.NumericTarget);
            FrozenOrchestrationIdentities frozen = FrozenOrchestrationIdentities.Prepare(
                new[] { Content.Identities, OrchestrationIdentityPublication.Create(new[] { contract.ContractId }) },
                Array.Empty<string>());
            SourceIdentityId = frozen.Table.Bind(contract.ContractId);
            Prepared = PreparedOrchestrationGraph<TScalar, TVector2, TVector3, TYaw, TQuaternion>.Prepare(Content, frozen, 0,
                    ReadOnlySpan<OrchestrationDefaultValue<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>>.Empty);
            Program = new OrchestrationProgram<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>>(Prepared.Program);
            StateTypes = new Type[Program.ValueSlotCount];
            for (int i = 0; i < StateTypes.Length; i++)
                StateTypes[i] = GraphNumericValues<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric>.ValueType(Program.DefaultValue(i), frozen.Table);
            var descriptors = new List<GraphVariableDescriptor>();
            var slots = new Dictionary<string, int>(StringComparer.Ordinal);
            string variableOwnerId = source.Variables.OwnerId;
            int graphIdentity = frozen.Table.Bind(variableOwnerId);
            foreach (ref readonly OrchestrationGraphVariable variable in Prepared.Variables.Span)
            {
                if (variable.GraphIdentityId != graphIdentity)
                    continue;
                string declaration = frozen.Table.Resolve(variable.DeclarationIdentityId);
                Type type = StateTypes[variable.ValueSlot];
                OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value = Program.DefaultValue(variable.ValueSlot);
                string identity = value.Kind == OrchestrationValueKind.Identity
                    ? frozen.Resolve(value.Identity) : default;
                descriptors.Add(new GraphVariableDescriptor(
                    new BtsmtlGraphVariableReference(variableOwnerId, declaration),
                    frozen.Table.Resolve(variable.KeyIdentityId), type,
                    GraphNumericValues<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric>.FromState(in value, type, identity), variable.IsPublic));
                slots.Add(declaration, variable.ValueSlot);
            }
            Variables = new GraphVariableContract(variableOwnerId, source.Variables.Revision, descriptors);
            PublishedSlots = new int[Variables.PublishedDescriptors.Count];
            if (PublishedSlots.Length != contract.Outputs.Count)
                throw new InvalidOperationException("事件图已编译变量与宿主发布合同数量不同。");
            for (int i = 0; i < PublishedSlots.Length; i++)
            {
                GraphVariableDescriptor variable = Variables.PublishedDescriptors[i];
                if (!contract.TryGetOutput(variable.Reference.VariableId, out GraphHostOutputDescriptor output) ||
                    output.ValueType != variable.ValueType)
                    throw new InvalidOperationException($"事件图变量 '{variable.Reference.VariableId}' 与宿主发布合同不匹配。");
                PublishedSlots[i] = slots[variable.Reference.VariableId];
            }

            Inputs = new InputBinding[Prepared.HostInputs.Length];
            int inputCapacity = 0;
            for (int i = 0; i < Inputs.Length; i++)
            {
                ref readonly OrchestrationGraphHostInput input = ref Prepared.HostInputs.Span[i];
                string id = frozen.Table.Resolve(input.InputIdentityId);
                int hostSlot = BindInput(id, frozen.Table.Resolve(input.ValueTypeIdentityId));
                Type type = contract.Inputs[hostSlot].ValueType;
                Inputs[i] = new InputBinding(hostSlot, input.InputSlot,
                    type.IsEnum ? frozen.Table.Bind(type.AssemblyQualifiedName) : 0, contract.Inputs[hostSlot]);
                inputCapacity = Math.Max(inputCapacity, input.InputSlot + 1);
            }
            InputCapacity = inputCapacity;
            OperationNodes = new string[Program.OperationCount];
            foreach (OrchestrationGraphSource map in Prepared.Sources.Span)
                if (map.Source.TargetKind == OrchestrationGraphSourceTargetKind.Operation)
                    OperationNodes[map.Source.TargetIndex] = map.Source.NodeId;
            Start = FindEntry("event:start");
            Update = FindEntry("event:update");

            int BindInput(string id, string typeIdentity)
            {
                for (int i = 0; i < contract.Inputs.Count; i++)
                {
                    GraphHostInputDescriptor input = contract.Inputs[i];
                    if (input.InputId != id)
                        continue;
                    if (GraphCompiledValues.TypeIdentity(input.ValueType) != typeIdentity)
                        throw new InvalidOperationException($"事件图输入 '{id}' 与宿主类型不匹配。");
                    return i;
                }
                throw new InvalidOperationException($"事件图输入 '{id}' 未由宿主声明。");
            }
        }

        internal OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> ToState(in GraphHostValue value, int enumTypeContentId,
            GraphTextStorage texts)
        {
            OrchestrationIdentityValue identity = default;
            if (value.Kind == GraphHostValueKind.Text)
            {
                identity = texts.Capture(value.TextValue);
            }
            return GraphNumericValues<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric>.ToState(in value, enumTypeContentId, in identity);
        }

        internal void PrepareInputs(IGraphHostContext host, Span<OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion>> destination,
            GraphTextStorage texts)
        {
            foreach (ref readonly InputBinding input in Inputs.AsSpan())
            {
                GraphHostValue value = host.ReadInput(input.HostSlot);
                if (value.Kind != input.ValueKind ||
                    input.ValueKind == GraphHostValueKind.Enum && value.EnumType != input.ValueType)
                    throw new InvalidOperationException($"事件图输入 '{input.InputId}' 与宿主声明类型不同。");
                destination[input.InputSlot] = ToState(in value, input.EnumTypeContentId, texts);
            }
        }

        internal GraphHostValue FromState(in OrchestrationValue<TScalar, TVector2, TVector3, TYaw, TQuaternion> value, Type enumType,
            GraphTextStorage texts)
        {
            string identity = value.Kind == OrchestrationValueKind.Identity
                ? texts.Resolve(value.Identity) : default;
            return GraphNumericValues<TScalar, TVector2, TVector3, TYaw, TQuaternion, TNumeric>.FromState(in value, enumType, identity);
        }

        OrchestrationExecutionEntry FindEntry(string identity)
        {
            foreach (ref readonly OrchestrationGraphEntry entry in Prepared.Entries.Span)
                if (Prepared.Identities.Table.Resolve(entry.EntryIdentityId) == identity)
                    return entry.Execution;
            return new OrchestrationExecutionEntry(-1, OrchestrationExecutionContract.Complete);
        }
    }
}
