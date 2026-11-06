using System;
using System.Linq;

namespace BTSMTL.Graphs
{
    public static class BtsmtlGraphValidator
    {
        public static GraphVariableContract Require(BtsmtlGraphRoot graph, GraphHostContract contract)
        {
            GraphVariableContract variables = graph.Variables.CreateContract();
            if (variables.GraphId != graph.Source.VariableOwnerId)
                throw new InvalidOperationException("图没有绑定正式文档声明的变量来源。");
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
    }
}
