using System;
using System.Collections.Generic;
using System.Linq;
using FlowCanvas;
using FlowCanvas.Macros;
using FlowCanvas.Nodes;
using NodeCanvas.Framework;

namespace BTSMTL.EventGraphs
{
    public static class EventGraphAssetValidator
    {
        public static EventGraphVariableContract Require(
            HostEventGraph graph,
            EventGraphHostContract contract)
        {
            if (graph == null)
                throw new ArgumentNullException(nameof(graph));
            if (contract == null)
                throw new ArgumentNullException(nameof(contract));
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(graph.AuthoringId))
                errors.Add("graph identity is missing");
            if (string.IsNullOrWhiteSpace(graph.ContentRevision))
                errors.Add("graph revision is missing");

            EventGraphVariableContract variables = null;
            try
            {
                variables = graph.BuildVariableContract();
            }
            catch (Exception exception)
            {
                errors.Add(exception.Message);
            }
            if (variables != null)
                ValidateOutputs(variables, contract, errors);

            var macros = new HashSet<Macro>();
            int startCount = 0;
            int updateCount = 0;
            ValidateNodes(
                graph,
                contract,
                graph.allNodes,
                macros,
                errors,
                ref startCount,
                ref updateCount,
                true);
            if (startCount != 1)
                errors.Add($"graph must contain exactly one StartEvent, found {startCount}");
            if (updateCount != 1)
                errors.Add($"graph must contain exactly one UpdateEvent, found {updateCount}");
            if (errors.Count != 0)
                throw new InvalidOperationException(
                    $"Event graph '{graph.AuthoringId}' is invalid: {string.Join("; ", errors)}");
            return variables;
        }

        static void ValidateOutputs(
            EventGraphVariableContract variables,
            EventGraphHostContract contract,
            List<string> errors)
        {
            for (int i = 0; i < variables.PublishedDescriptors.Count; i++)
            {
                EventGraphVariableDescriptor variable = variables.PublishedDescriptors[i];
                if (!contract.TryGetOutput(
                        variable.Reference.VariableId,
                        out EventGraphOutputDescriptor output))
                {
                    errors.Add(
                        $"variable '{variable.Reference.VariableId}' is not declared as a host output");
                    continue;
                }
                if (output.ValueType != variable.ValueType)
                {
                    errors.Add(
                        $"output '{variable.Reference.VariableId}' type '{output.ValueType.FullName}' does not match variable type '{variable.ValueType.FullName}'");
                }
            }
            if (contract.Outputs.Count != variables.PublishedDescriptors.Count)
                errors.Add(
                    $"host output count {contract.Outputs.Count} does not match published variable count {variables.PublishedDescriptors.Count}");
        }

        static void ValidateNodes(
            HostEventGraph root,
            EventGraphHostContract contract,
            IReadOnlyList<Node> nodes,
            HashSet<Macro> macros,
            List<string> errors,
            ref int startCount,
            ref int updateCount,
            bool rootGraph)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                Node node = nodes[i];
                if (node == null)
                {
                    errors.Add("graph contains a null node");
                    continue;
                }
                if (!EventGraphCapabilityCatalog.TryGet(
                        node.GetType(),
                        out EventGraphCapabilityKind capability))
                {
                    errors.Add($"node '{node.UID}' type '{node.GetType().FullName}' is not registered");
                    continue;
                }
                if (node is FlowNode nodeToValidate)
                    nodeToValidate.GatherPorts();
                switch (capability)
                {
                    case EventGraphCapabilityKind.InitializationEvent:
                        if (rootGraph)
                            startCount++;
                        break;
                    case EventGraphCapabilityKind.UpdateEvent:
                        if (rootGraph)
                            updateCount++;
                        ValidateUpdateEvent((UpdateEvent)node, errors);
                        break;
                    case EventGraphCapabilityKind.InstantSplit:
                        if (((Split)node).mode != Split.Mode.Instant)
                            errors.Add($"node '{node.UID}' must use Split.Instant");
                        break;
                    case EventGraphCapabilityKind.VariableGet:
                    case EventGraphCapabilityKind.VariableSet:
                        ValidateVariableNode(root, node, errors);
                        break;
                    case EventGraphCapabilityKind.HostInput:
                        ValidateHostInput(contract, node, errors);
                        break;
                    case EventGraphCapabilityKind.Macro:
                        if (node is MacroNodeWrapper wrapper)
                        {
                            if (wrapper.macro == null)
                                errors.Add($"macro node '{node.UID}' has no Macro asset");
                            else if (wrapper.macro.usesExternalExecution)
                                errors.Add($"macro node '{node.UID}' uses external execution");
                            else if (macros.Add(wrapper.macro))
                                ValidateMacro(
                                    root,
                                    contract,
                                    wrapper.macro,
                                    macros,
                                    errors);
                            else if (ContainsMacroReference(wrapper.macro, wrapper.macro))
                                errors.Add($"macro '{wrapper.macro.name}' is recursive");
                        }
                        break;
                    case EventGraphCapabilityKind.PureValue:
                        ValidateConnectedValuePorts(node, errors);
                        break;
                }
            }
        }

        static void ValidateMacro(
            HostEventGraph root,
            EventGraphHostContract contract,
            Macro macro,
            HashSet<Macro> macros,
            List<string> errors)
        {
            if (macro.inputDefinitions.Any(value => value == null || !IsSupportedMacroType(value.type)) ||
                macro.outputDefinitions.Any(value => value == null || !IsSupportedMacroType(value.type)))
                errors.Add($"macro '{macro.name}' has an unsupported parameter type");
            if (macro.allNodes.Any(node => node is EventGraphHostInputNodeMarker))
                errors.Add($"macro '{macro.name}' cannot read host inputs directly");
            foreach (Variable variable in macro.blackboard.variables.Values)
            {
                if (!variable.hasStableIdentity || variable.isDataBound || variable.isPropertyBound)
                    errors.Add($"macro variable '{variable.name}' must be a local declaration with a stable identity");
                if (!EventGraphValueKinds.IsSupported(variable.varType))
                    errors.Add($"macro variable '{variable.name}' uses unsupported type '{variable.varType.FullName}'");
            }
            int ignoredStartCount = 0;
            int ignoredUpdateCount = 0;
            ValidateNodes(
                root,
                contract,
                macro.allNodes,
                macros,
                errors,
                ref ignoredStartCount,
                ref ignoredUpdateCount,
                false);
        }

        static bool ContainsMacroReference(Macro root, Macro target)
        {
            var visited = new HashSet<Macro>();
            return ContainsMacroReference(root, target, visited);
        }

        static bool IsSupportedMacroType(Type type) =>
            type == typeof(Flow) || EventGraphValueKinds.IsSupported(type);

        static bool ContainsMacroReference(
            Macro current,
            Macro target,
            HashSet<Macro> visited)
        {
            if (!visited.Add(current))
                return false;
            foreach (MacroNodeWrapper wrapper in current.allNodes.OfType<MacroNodeWrapper>())
            {
                if (wrapper.macro == target)
                    return true;
                if (wrapper.macro != null &&
                    ContainsMacroReference(wrapper.macro, target, visited))
                    return true;
            }
            return false;
        }

        static void ValidateUpdateEvent(UpdateEvent update, List<string> errors)
        {
            if (update.updateInterval == null ||
                update.updateInterval.useBlackboard ||
                !float.IsFinite(update.updateInterval.value) ||
                update.updateInterval.value != 0f)
            {
                errors.Add($"UpdateEvent '{update.UID}' must have a literal zero interval");
            }
        }

        static void ValidateVariableNode(
            HostEventGraph graph,
            Node node,
            List<string> errors)
        {
            if (node is not ParameterVariableNode parameterNode ||
                parameterNode.parameter == null ||
                !parameterNode.parameter.useBlackboard ||
                string.IsNullOrEmpty(parameterNode.parameter.targetVariableID))
            {
                errors.Add($"variable node '{node.UID}' is not bound to a local Blackboard variable");
                return;
            }
            Variable variable = node.graph.blackboard.variables.Values.FirstOrDefault(value =>
                string.Equals(value.ID, parameterNode.parameter.targetVariableID, StringComparison.Ordinal));
            if (variable == null)
                errors.Add($"variable node '{node.UID}' references an external Blackboard variable");
            else if (variable.varType != parameterNode.parameter.varType)
                errors.Add($"variable node '{node.UID}' has a mismatched variable type");
            if (node is SetVariable<float> floatSet && floatSet.perSecond ||
                node is SetVariable<UnityEngine.Vector3> vectorSet && vectorSet.perSecond)
            {
                errors.Add($"Set node '{node.UID}' cannot use global per-second time");
            }
        }

        static void ValidateHostInput(
            EventGraphHostContract contract,
            Node node,
            List<string> errors)
        {
            if (node is not EventGraphHostInputNodeMarker inputNode)
            {
                errors.Add($"node '{node.UID}' is not a host input node");
                return;
            }
            string inputId = inputNode.InputId;
            Type valueType = inputNode.ValueType;
            if (string.IsNullOrWhiteSpace(inputId) ||
                !contract.TryGetInput(
                    inputId,
                    out EventGraphInputDescriptor descriptor))
            {
                errors.Add($"host input node '{node.UID}' references undeclared input '{inputId}'");
                return;
            }
            if (descriptor.ValueType != valueType)
                errors.Add($"host input node '{node.UID}' type does not match '{inputId}'");
        }

        static void ValidateConnectedValuePorts(Node node, List<string> errors)
        {
            if (node is not FlowNode flowNode)
                return;
            foreach (FlowCanvas.Port port in flowNode.GetAllPorts())
            {
                if (!port.IsValuePort() ||
                    !port.isConnected ||
                    EventGraphValueKinds.IsSupported(port.type))
                    continue;
                errors.Add(
                    $"node '{node.UID}' has a connected unsupported value port '{port.ID}'");
            }
        }
    }

    public interface EventGraphHostInputNodeMarker
    {
        string InputId { get; }
        Type ValueType { get; }
        void ConfigureInput(string inputId);
    }
}
