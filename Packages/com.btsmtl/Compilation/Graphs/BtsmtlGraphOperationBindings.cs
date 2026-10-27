using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
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
        public BtsmtlGraphValuePortBinding Input(string nodeId, string portId) => m_Inputs[(nodeId, portId)];
        public BtsmtlGraphValuePortBinding Output(string nodeId, string portId) => m_Outputs[(nodeId, portId)];

        public void AddNode(CompileNode node, OperationHandle operation)
        {
            m_Nodes.Add(node.Id, operation);
            m_FlowSources.Add(node.Id, operation);
            foreach (string input in node.FlowInputs)
                m_FlowInputs.Add((node.Id, input), operation);
            foreach (string output in node.FlowOutputs)
                m_FlowOutputs.Add((node.Id, output), operation);
        }

        public void SetFlowSource(CompileNode node, OperationHandle operation)
        {
            m_FlowSources[node.Id] = operation;
            foreach (string output in node.FlowOutputs)
                m_FlowOutputs[(node.Id, output)] = operation;
        }

        public void AddValueInput(CompileNode node, string authoringPort, OperationHandle operation, string compiledPort) =>
            m_Inputs.Add((node.Id, authoringPort), new BtsmtlGraphValuePortBinding(operation, compiledPort));

        public void AddValueOutput(CompileNode node, string authoringPort, OperationHandle operation, string compiledPort) =>
            m_Outputs.Add((node.Id, authoringPort), new BtsmtlGraphValuePortBinding(operation, compiledPort));

        // 叶子节点的全部端口登记到同一操作，值端口使用编译身份。
        public void AddLeaf(CompileNode node, OperationHandle operation)
        {
            AddNode(node, operation);
            foreach (CompileValuePort input in node.ValueInputs)
                AddValueInput(node, input.Id, operation, input.CompiledId);
            foreach (CompileValuePort output in node.ValueOutputs)
                AddValueOutput(node, output.Id, operation, output.CompiledId);
        }
    }
}
