using System;
using System.Collections.Generic;
using BTSMTL;
using ThirdPersonSimulation;

namespace BTSMTL.EventGraphs
{
    sealed class EventGraphCompiledBindings
    {
        internal readonly Float32GraphBindings Runtime;
        internal readonly EventGraphVariableContract Variables;
        internal readonly Type[] StateTypes;

        internal EventGraphCompiledBindings(HostEventGraph source, EventGraphHostContract contract)
        {
            Float32GraphExecutionData data = source.RequireExecutionData();
            Runtime = new Float32GraphBindings(data, source.AuthoringId, BindInput);
            StateTypes = new Type[data.StateSlots.Count];
            for (int i = 0; i < StateTypes.Length; i++)
                StateTypes[i] = EventGraphCompiledValues.ValueType(data.DefaultStateValue(i));
            var descriptors = new List<EventGraphVariableDescriptor>();
            foreach (ProgramExecutionSource map in Runtime.Variables)
            {
                int slot = map.TargetIndex;
                ProgramCatalogEntry declaration = Runtime.VariableDeclarations[slot];
                descriptors.Add(new EventGraphVariableDescriptor(
                    new BtsmtlGraphVariableReference(source.AuthoringId, map.DeclarationId),
                    Runtime.Field(declaration, "Key").Text.ToString(), StateTypes[slot],
                    EventGraphCompiledValues.FromState(data.DefaultStateValue(slot), StateTypes[slot]),
                    Runtime.Field(declaration, "Public").Boolean));
            }
            Variables = new EventGraphVariableContract(source.AuthoringId, source.ExecutionIdentity.SourceRevision, descriptors);
            if (Runtime.PublishedSlots.Length != contract.Outputs.Count)
                throw new InvalidOperationException("事件图已编译变量与宿主发布合同数量不同。");
            foreach (EventGraphVariableDescriptor variable in Variables.PublishedDescriptors)
                if (!contract.TryGetOutput(variable.Reference.VariableId, out EventGraphOutputDescriptor output) ||
                    output.ValueType != variable.ValueType)
                    throw new InvalidOperationException($"事件图变量 '{variable.Reference.VariableId}' 与宿主发布合同不匹配。");

            int BindInput(string id, string type)
            {
                for (int i = 0; i < contract.Inputs.Count; i++)
                {
                    EventGraphInputDescriptor input = contract.Inputs[i];
                    if (input.InputId != id)
                        continue;
                    if (GraphValueTypeIdentity.Of(BtsmtlGraphValueTypes.StateKind(input.ValueType),
                        input.ValueType.IsEnum ? input.ValueType.AssemblyQualifiedName : "") != type)
                        throw new InvalidOperationException($"事件图输入 '{id}' 与宿主类型不匹配。");
                    return i;
                }
                throw new InvalidOperationException($"事件图输入 '{id}' 未由宿主声明。");
            }
        }
    }
}
