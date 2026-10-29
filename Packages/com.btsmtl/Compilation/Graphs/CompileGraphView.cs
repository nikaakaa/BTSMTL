using System;
using System.Collections.Generic;
using System.Globalization;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    // 图编译的值类型：编译值种类、平台全名（声明目录的 ValueType 字段）、枚举身份与该类型的零值。
    public sealed class CompileValueType
    {
        public CompileValueType(SemanticValueKind kind, string fullName, object defaultValue, string enumIdentity = null)
        {
            if (kind == SemanticValueKind.Enum && string.IsNullOrEmpty(enumIdentity))
                throw new ArgumentException("枚举值类型需要枚举身份。", nameof(enumIdentity));
            Kind = kind;
            FullName = fullName ?? throw new ArgumentNullException(nameof(fullName));
            DefaultValue = defaultValue;
            EnumIdentity = enumIdentity;
        }

        public SemanticValueKind Kind { get; }
        public string FullName { get; }
        public object DefaultValue { get; }
        public string EnumIdentity { get; }
        public bool IsEnum => Kind == SemanticValueKind.Enum;

        // 作者值转为编译字面量：枚举按枚举身份与整数值，其余保持原值。
        public object ToSemanticLiteral(object value) => IsEnum
            ? new SemanticEnumValue(EnumIdentity, Convert.ToInt32(value, CultureInfo.InvariantCulture))
            : value;

        public bool SameType(CompileValueType other) => other != null &&
            Kind == other.Kind && string.Equals(FullName, other.FullName, StringComparison.Ordinal);
    }

    public enum CompileNodeRole : byte
    {
        Leaf,
        MacroInput,
        MacroOutput,
        MacroCall,
        Variable
    }

    // 值端口：作者身份、编译身份与类型；未连接输入的作者默认值由 DefaultValue 给出。
    public sealed class CompileValuePort
    {
        public CompileValuePort(string id, string compiledId, CompileValueType type, bool isConnected = false,
            bool isRequired = false, object defaultValue = null)
        {
            Id = id;
            CompiledId = compiledId;
            Type = type ?? throw new ArgumentNullException(nameof(type));
            IsConnected = isConnected;
            IsRequired = isRequired;
            DefaultValue = defaultValue;
        }

        public string Id { get; }
        public string CompiledId { get; }
        public CompileValueType Type { get; }
        public bool IsConnected { get; }
        public bool IsRequired { get; }
        public object DefaultValue { get; }
    }

    public sealed class CompileEdge
    {
        public CompileEdge(string id, string sourceType, CompileNode source, string sourcePortId,
            string targetNodeId, string targetPortId, bool isValue)
        {
            Id = id;
            SourceType = sourceType;
            Source = source;
            SourcePortId = sourcePortId;
            TargetNodeId = targetNodeId;
            TargetPortId = targetPortId;
            IsValue = isValue;
        }

        public string Id { get; }
        public string SourceType { get; }
        public CompileNode Source { get; }
        public string SourcePortId { get; }
        public string TargetNodeId { get; }
        public string TargetPortId { get; }
        public bool IsValue { get; }
        public CompileNode Target => Source.Graph.Node(TargetNodeId);
    }

    // 子图参数：执行参数不进入值合同。
    public readonly struct CompileGraphParameter
    {
        public CompileGraphParameter(string id, string name, CompileValueType type, bool isFlow)
        {
            Id = id;
            Name = name;
            Type = type;
            IsFlow = isFlow;
        }

        public string Id { get; }
        public string Name { get; }
        public CompileValueType Type { get; }
        public bool IsFlow { get; }
    }

    // 图节点的编译视图：端口按平台注册顺序排列，出边按作者顺序排列；编译描述由图来源按节点能力给出。
    public sealed class CompileNode
    {
        readonly Func<CompileNode, SimulationNodeEmission> m_Describe;
        readonly Func<GraphNodeFields> m_ReadFields;
        GraphNodeFields m_Fields;
        List<CompileEdge> m_OutEdges;

        // fields 在首次读取作者字段时调用一次；节点没有作者字段时可为 null。
        public CompileNode(string id, string sourceType, string kind, CompileNodeRole role,
            IReadOnlyList<string> flowInputs, IReadOnlyList<string> flowOutputs,
            IReadOnlyList<CompileValuePort> valueInputs, IReadOnlyList<CompileValuePort> valueOutputs,
            Func<GraphNodeFields> fields, Func<CompileNode, SimulationNodeEmission> describe)
        {
            Id = id;
            SourceType = sourceType;
            Kind = kind;
            Role = role;
            FlowInputs = flowInputs;
            FlowOutputs = flowOutputs;
            ValueInputs = valueInputs;
            ValueOutputs = valueOutputs;
            m_ReadFields = fields;
            m_Describe = describe;
        }

        public string Id { get; }
        public string SourceType { get; }
        public string Kind { get; }
        public CompileNodeRole Role { get; }
        public IReadOnlyList<string> FlowInputs { get; }
        public IReadOnlyList<string> FlowOutputs { get; }
        public IReadOnlyList<CompileValuePort> ValueInputs { get; }
        public IReadOnlyList<CompileValuePort> ValueOutputs { get; }
        public GraphNodeFields Fields => m_Fields ??= m_ReadFields?.Invoke() ??
            throw new InvalidOperationException($"图节点 '{Id}' 没有作者字段。");
        public CompileGraph Graph { get; private set; }
        public IReadOnlyList<CompileEdge> OutEdges => m_OutEdges;

        // 子图调用的被调图；变量节点的声明 owner、变量与写入方向。
        public CompileGraph Callee { get; private set; }
        public string VariableOwnerId { get; private set; }
        public string VariableId { get; private set; }
        public bool VariableWrites { get; private set; }

        public SimulationNodeEmission Describe() => m_Describe(this);

        public CompileValuePort Input(string id)
        {
            foreach (CompileValuePort port in ValueInputs)
                if (port.Id == id)
                    return port;
            throw new KeyNotFoundException($"图节点 '{Id}' 没有值输入 '{id}'。");
        }

        public CompileNode WithCallee(CompileGraph callee)
        {
            Callee = callee;
            return this;
        }

        public CompileNode WithVariable(string ownerId, string variableId, bool writes)
        {
            VariableOwnerId = ownerId;
            VariableId = variableId;
            VariableWrites = writes;
            return this;
        }

        internal void Attach(CompileGraph graph, List<CompileEdge> edges)
        {
            Graph = graph;
            m_OutEdges = edges;
        }
    }

    // 图的编译视图：节点按身份排序；子图带入口与结果参数；变量声明由图来源给出。
    public sealed class CompileGraph
    {
        readonly Dictionary<string, CompileNode> m_Nodes = new(StringComparer.Ordinal);

        public CompileGraph(string id, string sourceType, IReadOnlyList<CompileGraphParameter> inputs = null,
            IReadOnlyList<CompileGraphParameter> outputs = null)
        {
            Id = id;
            SourceType = sourceType;
            Inputs = inputs ?? Array.Empty<CompileGraphParameter>();
            Outputs = outputs ?? Array.Empty<CompileGraphParameter>();
        }

        public string Id { get; }
        public string SourceType { get; }
        public IReadOnlyList<CompileGraphParameter> Inputs { get; }
        public IReadOnlyList<CompileGraphParameter> Outputs { get; }
        public IReadOnlyList<CompileNode> Nodes { get; private set; } = Array.Empty<CompileNode>();
        public bool IsMacro => Find(CompileNodeRole.MacroInput) != null;

        public CompileNode Node(string id) => m_Nodes[id];

        public CompileNode Find(CompileNodeRole role)
        {
            CompileNode found = null;
            foreach (CompileNode node in Nodes)
                if (node.Role == role)
                {
                    if (found != null)
                        throw new InvalidOperationException($"图 '{Id}' 有多个 {role} 节点。");
                    found = node;
                }
            return found;
        }

        // nodes 与 edges 由图来源一次给出；出边保持节点内的作者顺序。
        public CompileGraph Complete(IEnumerable<CompileNode> nodes,
            Func<CompileNode, IEnumerable<CompileEdge>> outEdges)
        {
            var ordered = new List<CompileNode>(nodes);
            ordered.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
            foreach (CompileNode node in ordered)
                m_Nodes.Add(node.Id, node);
            Nodes = ordered;
            foreach (CompileNode node in ordered)
                node.Attach(this, new List<CompileEdge>(outEdges(node)));
            return this;
        }
    }
}
