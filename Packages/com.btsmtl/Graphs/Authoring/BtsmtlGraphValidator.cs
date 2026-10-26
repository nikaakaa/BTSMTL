using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Blackboard;
using FlowCanvas;
using FlowCanvas.Macros;
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
            var entryIds = new HashSet<string>(StringComparer.Ordinal);
            var entryNodes = new HashSet<string>(StringComparer.Ordinal);
            RequireContent(graph, graph, variables, contract, "graph:" + graph.AuthoringId, new List<BtsmtlMacroGraph>());
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
            foreach (string domain in graph.Domains)
                if (!contract.Domains.Contains(domain, StringComparer.Ordinal))
                    throw new InvalidOperationException($"图使用的领域 '{domain}' 未由宿主合同 '{contract.ContractId}' 提供。");
            return variables;
        }

        // 图根与每个子图出现共用的内容校验；子图节点同时经子图宿主声明与实际调用方图根复核。
        static void RequireContent(BtsmtlGraphRoot root, FlowGraph graph, GraphVariableContract variables,
            GraphHostContract contract, string path, List<BtsmtlMacroGraph> stack)
        {
            var declarations = variables.Descriptors.ToDictionary(value => value.Reference.VariableId,
                StringComparer.Ordinal);
            var nodeIds = new HashSet<string>(StringComparer.Ordinal);
            graph.Validate();
            foreach (Node node in graph.allNodes)
            {
                string nodePath = $"{path}/node:{node.UID}";
                bool anchor = node is BtsmtlMacroInputNode || node is BtsmtlMacroOutputNode;
                if (!nodeIds.Add(node.UID) || !graph.CanAuthorNodeType(node.GetType()) ||
                    !anchor && !root.CanAuthorNodeType(node.GetType()))
                    throw new InvalidOperationException($"{nodePath}: 节点身份或类型不属于调用方图的正式作者合同。");
                if (node is IBtsmtlVariableFlowNode variable)
                {
                    if (variable.OwnerId != variables.GraphId ||
                        !declarations.TryGetValue(variable.VariableId, out GraphVariableDescriptor declaration) ||
                        declaration.ValueType != variable.ValueType)
                        throw new InvalidOperationException($"{nodePath}: 没有引用所属 owner 中同类型的变量声明。");
                }
                if (node is IBtsmtlHostInputFlowNode input &&
                    (!contract.TryGetInput(input.InputId, out GraphHostInputDescriptor descriptor) ||
                     descriptor.ValueType != input.ValueType))
                    throw new InvalidOperationException($"{nodePath}: 输入 '{input.InputId}' 未由宿主以相同类型声明。");
                foreach (BinderConnection connection in node.outConnections.Cast<BinderConnection>())
                    if (connection.sourceNode.graph != graph || connection.targetNode.graph != graph ||
                        connection.sourcePort.type != connection.targetPort.type ||
                        connection.sourcePort.IsFlowPort() != connection.targetPort.IsFlowPort())
                        throw new InvalidOperationException($"{path}/edge:{connection.UID}: 图连接的端点或类型不匹配。");
                if (node is MacroNodeWrapper call)
                    RequireCall(root, call, contract, $"{path}/call:{call.UID}", stack);
            }
        }

        static void RequireCall(BtsmtlGraphRoot root, MacroNodeWrapper call, GraphHostContract contract,
            string path, List<BtsmtlMacroGraph> stack)
        {
            if (call.macro is not BtsmtlMacroGraph macro)
                throw new InvalidOperationException($"{path}: 子图调用缺少 BTSMTL 子图目标。");
            if (stack.Contains(macro))
                throw new InvalidOperationException($"{path}: 子图 '{macro.name}' 递归调用自身（" +
                    string.Join(" → ", stack.Select(value => value.name).Append(macro.name)) + "）。");
            if (string.IsNullOrWhiteSpace(macro.AuthoringId) || string.IsNullOrWhiteSpace(macro.ContentRevision))
                throw new InvalidOperationException($"{path}: 子图 '{macro.name}' 缺少稳定身份或内容修订。");
            if (macro.Host is not BtsmtlGraphMacroHost host)
                throw new InvalidOperationException($"{path}: 子图 '{macro.name}' 未声明通用图宿主准入。");
            foreach (string domain in host.Domains)
                if (!root.Domains.Contains(domain, StringComparer.Ordinal))
                    throw new InvalidOperationException($"{path}: 子图 '{macro.name}' 的领域 '{domain}' 不属于调用方图。");
            BtsmtlMacroInterface.ValidateCall(call, macro, path);
            BtsmtlGraphVariableDeclarations declarations = macro.Variables;
            declarations.Validate();
            foreach (BtsmtlBlackboardDeclaration declaration in declarations.BlackboardDeclarations)
                if (declaration.Scope != PipelineBlackboardVariableScope.Graph ||
                    declaration.Lifetime != PipelineBlackboardVariableLifetime.GraphInstance)
                    throw new InvalidOperationException(
                        $"{path}: 子图局部变量 '{declaration.VariableId}' 必须是图作用域、图实例生命周期。");
            if (!contract.BlackboardScopes.Contains(PipelineBlackboardVariableScope.Graph) &&
                declarations.BlackboardDeclarations.Count != 0)
                throw new InvalidOperationException($"{path}: 宿主合同 '{contract.ContractId}' 不提供子图局部变量所需的图作用域。");
            GraphVariableContract variables = declarations.CreateContract();
            if (variables.PublishedDescriptors.Count != 0)
                throw new InvalidOperationException($"{path}: 子图局部变量不能发布到宿主。");
            stack.Add(macro);
            try { RequireContent(root, macro, variables, contract, path, stack); }
            finally { stack.RemoveAt(stack.Count - 1); }
        }
    }
}
