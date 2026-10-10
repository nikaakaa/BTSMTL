using System;
using System.Collections.Generic;
using ThirdPersonSimulation;

namespace BTSMTL.EventGraphs
{
    sealed class EventGraphCompiledBindings
    {
        internal readonly Float32GraphExecutionData Data;
        internal readonly EventGraphVariableContract Variables;
        internal readonly EventGraphInputDescriptor[] Inputs;
        internal readonly CanonicalTextValue[] InputEnumIdentities;
        internal readonly int[] OperationSlots;
        internal readonly int[] PublishedSlots;
        internal readonly Type[] StateTypes;
        internal readonly string[] OperationNodes;
        internal readonly OperationHandle Start;
        internal readonly OperationHandle Update;

        internal EventGraphCompiledBindings(HostEventGraph source, EventGraphHostContract contract)
        {
            Data = source.RequireExecutionData();
            Inputs = new EventGraphInputDescriptor[Data.Operations.Count];
            InputEnumIdentities = new CanonicalTextValue[Data.Operations.Count];
            OperationSlots = new int[Data.Operations.Count];
            OperationNodes = new string[Data.Operations.Count];
            Array.Fill(OperationSlots, -1);
            StateTypes = new Type[Data.StateSlots.Count];
            for (int i = 0; i < StateTypes.Length; i++)
                StateTypes[i] = EventGraphCompiledValues.ValueType(Data.DefaultStateValue(i));
            var descriptors = new List<EventGraphVariableDescriptor>();
            var rootSlots = new Dictionary<string, int>(StringComparer.Ordinal);
            ProgramCatalogEntry[] declarations = Data.CatalogIndex.BindBlackboardDeclarations(Data.StateSlots, Data.References);
            foreach (ProgramSourceMapEntry map in Data.SourceMap)
            {
                if (map.TargetKind == ProgramSourceTargetKind.Operation)
                    OperationNodes[map.TargetIndex] = map.NodeId;
                if (map.TargetKind != ProgramSourceTargetKind.StateSlot || map.GraphId != source.AuthoringId ||
                    Data.StateSlots[map.TargetIndex].Semantic != ProgramStateSemantic.BlackboardValue)
                    continue;
                int slot = map.TargetIndex;
                ProgramCatalogEntry declaration = declarations[slot];
                descriptors.Add(new EventGraphVariableDescriptor(
                    new BtsmtlGraphVariableReference(source.AuthoringId, map.DeclarationId),
                    Field(declaration, "Key").Text.ToString(), StateTypes[slot],
                    EventGraphCompiledValues.FromState(Data.DefaultStateValue(slot), StateTypes[slot]),
                    Field(declaration, "Public").Boolean));
                rootSlots.Add(map.DeclarationId, slot);
            }
            Variables = new EventGraphVariableContract(source.AuthoringId, source.ContentRevision, descriptors);
            PublishedSlots = new int[Variables.PublishedDescriptors.Count];
            if (PublishedSlots.Length != contract.Outputs.Count)
                throw new InvalidOperationException("事件图已编译变量与宿主发布合同数量不同。");
            for (int i = 0; i < PublishedSlots.Length; i++)
            {
                EventGraphVariableDescriptor variable = Variables.PublishedDescriptors[i];
                if (!contract.TryGetOutput(variable.Reference.VariableId, out EventGraphOutputDescriptor output) ||
                    output.ValueType != variable.ValueType)
                    throw new InvalidOperationException($"事件图变量 '{variable.Reference.VariableId}' 与宿主发布合同不匹配。");
                PublishedSlots[i] = rootSlots[variable.Reference.VariableId];
            }
            Start = OperationHandle.Invalid;
            Update = OperationHandle.Invalid;
            foreach (ProgramReference reference in Data.References)
            {
                if (reference.HasSourceOperation && reference.Kind == ProgramReferenceKind.StateSlot)
                    OperationSlots[reference.SourceOperation.Value] = reference.TargetIndex;
                if (!reference.HasSourceOperation && reference.Kind == ProgramReferenceKind.Operation)
                {
                    if (reference.Identity == "event:start") Start = new OperationHandle(reference.TargetIndex);
                    if (reference.Identity == "event:update") Update = new OperationHandle(reference.TargetIndex);
                }
            }
            foreach (SimulationOperation operation in Data.Operations)
            {
                if (operation.Code == SimulationOperationCode.Cycle)
                    OperationSlots[operation.Handle.Value] = Data.Topology.RequireOperationStateSlot(
                        operation.Handle, ProgramStateSemantic.RunnableChildCursor);
                if (operation.Code != SimulationOperationCode.HostInput)
                    continue;
                if (!contract.TryGetInput(operation.Text0, out EventGraphInputDescriptor input) ||
                    input.ValueType.AssemblyQualifiedName != Data.Constants[operation.ConstantReferences[0]].Text.ToString())
                    throw new InvalidOperationException($"事件图输入 '{operation.Text0}' 与宿主合同不匹配。");
                Inputs[operation.Handle.Value] = input;
                if (input.ValueKind == EventGraphValueKind.Enum)
                    InputEnumIdentities[operation.Handle.Value] = CanonicalTextValue.FromString(input.ValueType.AssemblyQualifiedName);
            }
        }

        ProgramConstant Field(ProgramCatalogEntry entry, string name)
        {
            foreach (ProgramCatalogField field in entry.Fields)
                if (field.Name == name)
                    return Data.Constants[field.ConstantIndex];
            throw new InvalidOperationException($"变量声明 '{entry.Identity}' 缺少 '{name}'。");
        }
    }
}
