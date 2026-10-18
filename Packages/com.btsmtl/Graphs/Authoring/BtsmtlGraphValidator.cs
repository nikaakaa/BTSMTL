using System;
using System.Collections.Generic;
using System.Linq;
using FlowCanvas;
using NodeCanvas.Framework;

namespace BTSMTL.Graphs
{
    public static class BtsmtlGraphValidator
    {
        public static GraphVariableContract Require(BtsmtlGraphRoot graph, GraphHostContract contract)
        {
            if (string.IsNullOrWhiteSpace(graph.AuthoringId) || string.IsNullOrWhiteSpace(graph.ContentRevision))
                throw new InvalidOperationException("图必须声明稳定身份和内容修订。");
            IBtsmtlGraphVariableOwner owner = graph.VariableOwner;
            if (owner == null || !owner.OwnerAsset)
                throw new InvalidOperationException("图变量缺少正式声明 owner。");
            GraphVariableContract variables = owner.VariableDeclarations.CreateContract();
            var declarations = variables.Descriptors.ToDictionary(value => value.Reference.VariableId,
                StringComparer.Ordinal);
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            var entryNodes = new HashSet<string>(StringComparer.Ordinal);
            graph.Validate();
            foreach (Node node in graph.allNodes)
            {
                if (!nodeIds.Add(node.UID) || !graph.CanAuthorNodeType(node.GetType()))
                    throw new InvalidOperationException($"图节点 '{node.UID}' 的身份或类型不属于正式作者合同。");
                if (node is IBtsmtlVariableFlowNode variable)
                {
                    if (variable.OwnerId != variables.GraphId ||
                        !declarations.TryGetValue(variable.VariableId, out GraphVariableDescriptor declaration) ||
                        declaration.ValueType != variable.ValueType)
                        throw new InvalidOperationException($"节点 '{node.UID}' 没有引用所属 owner 中同类型的变量声明。");
                }
                if (node is IBtsmtlHostInputFlowNode input &&
                    (!contract.TryGetInput(input.InputId, out GraphHostInputDescriptor descriptor) ||
                     descriptor.ValueType != input.ValueType))
                    throw new InvalidOperationException($"节点 '{node.UID}' 的输入 '{input.InputId}' 未由宿主以相同类型声明。");
                foreach (BinderConnection connection in node.outConnections.Cast<BinderConnection>())
                    if (connection.sourceNode.graph != graph || connection.targetNode.graph != graph ||
                        connection.sourcePort.type != connection.targetPort.type ||
                        connection.sourcePort.IsFlowPort() != connection.targetPort.IsFlowPort())
                        throw new InvalidOperationException($"图连接 '{connection.UID}' 的端点或类型不匹配。");
            }
            foreach (BtsmtlGraphEntryDeclaration entry in graph.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.EntryId) || !entryIds.Add(entry.EntryId) ||
                    !entryNodes.Add(entry.NodeId) ||
                    !Enum.IsDefined(typeof(BTSMTL.Orchestration.OrchestrationExecutionContract), entry.Contract) ||
                    graph.allNodes.SingleOrDefault(node => node.UID == entry.NodeId) is not BtsmtlEntryFlowNode)
                    throw new InvalidOperationException($"图入口 '{entry.EntryId}' 没有唯一节点和有效的执行合同。");
            }
            if (entryIds.Count == 0 || graph.allNodes.OfType<BtsmtlEntryFlowNode>().Any(node => !entryNodes.Contains(node.UID)))
                throw new InvalidOperationException("每个图入口节点必须由根声明唯一的执行入口。");
            if (variables.PublishedDescriptors.Count != contract.Outputs.Count)
                throw new InvalidOperationException("图变量声明与宿主发布合同数量不同。");
            foreach (GraphVariableDescriptor variable in variables.PublishedDescriptors)
                if (!contract.TryGetOutput(variable.Reference.VariableId, out GraphHostOutputDescriptor output) ||
                    output.ValueType != variable.ValueType)
                    throw new InvalidOperationException($"变量 '{variable.Reference.VariableId}' 与宿主输出合同不匹配。");
            return variables;
        }
    }
}
