using System;
using System.Collections.Generic;
using FlowCanvas;
using BTSMTL;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Graphs.Editor
{
    public readonly struct BtsmtlGraphValuePortBinding
    {
        public BtsmtlGraphValuePortBinding(OperationHandle operation, string portId)
        {
            if (!operation.IsValid || string.IsNullOrWhiteSpace(portId))
                throw new ArgumentException("编译值端口必须有操作及端口身份。");
            Operation = operation;
            PortId = portId;
        }

        public OperationHandle Operation { get; }
        public string PortId { get; }
    }

    public sealed class BtsmtlGraphOperationBindings
    {
        readonly Func<FlowNode, string, OrchestrationExtensionPortDirection, string> m_MapPort;
        public BtsmtlGraphOperationBindings(
            Func<FlowNode, string, OrchestrationExtensionPortDirection, string> mapPort = null) => m_MapPort = mapPort;
        readonly Dictionary<string, OperationHandle> m_Nodes = new(StringComparer.Ordinal);
        readonly Dictionary<string, OperationHandle> m_FlowSources = new(StringComparer.Ordinal);
        readonly Dictionary<(string Node, string Port), OperationHandle> m_FlowInputs = new();
        readonly Dictionary<(string Node, string Port), OperationHandle> m_FlowOutputs = new();
        readonly Dictionary<(string Node, string Port), BtsmtlGraphValuePortBinding> m_Inputs = new();
        readonly Dictionary<(string Node, string Port), BtsmtlGraphValuePortBinding> m_Outputs = new();

        public IReadOnlyDictionary<string, OperationHandle> Nodes => m_Nodes;
        public OperationHandle Node(string identity) => m_Nodes[identity];
        public OperationHandle FlowSource(string identity) => m_FlowSources[identity];
        public OperationHandle FlowInput(string node, string port) => m_FlowInputs[(node, port)];
        public OperationHandle FlowOutput(string node, string port) => m_FlowOutputs[(node, port)];
        public void SetFlowInput(FlowNode node, string port, OperationHandle operation) => m_FlowInputs[(node.UID, port)] = operation;
        public void SetFlowOutput(FlowNode node, string port, OperationHandle operation) => m_FlowOutputs[(node.UID, port)] = operation;
        public BtsmtlGraphValuePortBinding Input(string nodeId, string portId) => m_Inputs[(nodeId, portId)];
        public BtsmtlGraphValuePortBinding Output(string nodeId, string portId) => m_Outputs[(nodeId, portId)];

        public void AddNode(FlowNode node, OperationHandle operation)
        {
            m_Nodes.Add(node.UID, operation);
            m_FlowSources.Add(node.UID, operation);
            foreach (FlowInput input in node.GetInputFlowPorts())
                m_FlowInputs.Add((node.UID, input.ID), operation);
            foreach (FlowOutput output in node.GetOutputFlowPorts())
                m_FlowOutputs.Add((node.UID, output.ID), operation);
        }

        public void SetFlowSource(FlowNode node, OperationHandle operation)
        {
            m_FlowSources[node.UID] = operation;
            foreach (FlowOutput output in node.GetOutputFlowPorts())
                SetFlowOutput(node, output.ID, operation);
        }

        public void AddValueInput(FlowNode node, string authoringPort, OperationHandle operation, string compiledPort) =>
            m_Inputs.Add((node.UID, authoringPort), new BtsmtlGraphValuePortBinding(operation, compiledPort));

        public void AddValueOutput(FlowNode node, string authoringPort, OperationHandle operation, string compiledPort) =>
            m_Outputs.Add((node.UID, authoringPort), new BtsmtlGraphValuePortBinding(operation, compiledPort));

        public void AddLeaf(FlowNode node, OperationHandle operation)
        {
            AddNode(node, operation);
            BtsmtlNativeValueNodeCatalog.TryGetNode(node.GetType(), out BtsmtlNativeValueNodeDefinition native);
            foreach (ValueInput input in node.GetInputValuePorts())
                AddValueInput(node, input.ID, operation, native != null ? native.Input(input.ID)
                    : m_MapPort == null ? input.ID
                        : m_MapPort(node, input.ID, OrchestrationExtensionPortDirection.Input));
            foreach (ValueOutput output in node.GetOutputValuePorts())
                AddValueOutput(node, output.ID, operation, native != null ? native.Output(output.ID)
                    : m_MapPort == null ? output.ID
                        : m_MapPort(node, output.ID, OrchestrationExtensionPortDirection.Output));
        }
    }
}
