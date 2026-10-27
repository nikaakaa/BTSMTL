using System;
using System.Collections;
using System.Collections.Generic;

namespace BTSMTL.Authoring.Graph
{
    // 节点端口的平台无关声明：Unity 节点按此注册端口，独立编译按此建立编译视图。
    // 值类型使用作者值类型身份（bool、float、vector3 等）；DefaultFieldId 非空时未连接输入取该作者字段的值。
    public sealed class GraphNodePortDeclaration
    {
        public GraphNodePortDeclaration(GraphAuthoringPortDirection direction, bool isFlow, string id,
            string displayName, string valueTypeId, bool hasDefault, object defaultValue, string defaultFieldId)
        {
            Direction = direction;
            IsFlow = isFlow;
            Id = GraphAuthoringIdentity.Require(id, nameof(id));
            DisplayName = displayName ?? string.Empty;
            ValueTypeId = isFlow ? "flow" : GraphAuthoringIdentity.Require(valueTypeId, nameof(valueTypeId));
            HasDefault = hasDefault;
            DefaultValue = defaultValue;
            DefaultFieldId = defaultFieldId ?? string.Empty;
        }

        public GraphAuthoringPortDirection Direction { get; }
        public bool IsFlow { get; }
        public string Id { get; }
        public string DisplayName { get; }
        public string ValueTypeId { get; }
        public bool HasDefault { get; }
        public object DefaultValue { get; }
        public string DefaultFieldId { get; }
        public bool IsInput => Direction == GraphAuthoringPortDirection.Input;
    }

    // 一个节点能力的全部固定端口，按注册顺序排列；顺序决定编译视图的端口顺序。
    public sealed class GraphNodePorts : IReadOnlyList<GraphNodePortDeclaration>
    {
        public static readonly GraphNodePorts None = new();

        readonly List<GraphNodePortDeclaration> m_Ports = new();

        public int Count => m_Ports.Count;
        public GraphNodePortDeclaration this[int index] => m_Ports[index];
        public IEnumerator<GraphNodePortDeclaration> GetEnumerator() => m_Ports.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public GraphNodePorts FlowIn(string id, string name) =>
            Add(new GraphNodePortDeclaration(GraphAuthoringPortDirection.Input, true, id, name, null, false, null, null));

        public GraphNodePorts FlowOut(string id, string name) =>
            Add(new GraphNodePortDeclaration(GraphAuthoringPortDirection.Output, true, id, name, null, false, null, null));

        public GraphNodePorts ValueIn(string id, string name, string valueTypeId) =>
            Add(new GraphNodePortDeclaration(GraphAuthoringPortDirection.Input, false, id, name, valueTypeId, false, null, null));

        public GraphNodePorts ValueIn(string id, string name, string valueTypeId, object defaultValue) =>
            Add(new GraphNodePortDeclaration(GraphAuthoringPortDirection.Input, false, id, name, valueTypeId, true, defaultValue, null));

        public GraphNodePorts FieldValueIn(string id, string name, string valueTypeId, string defaultFieldId) =>
            Add(new GraphNodePortDeclaration(GraphAuthoringPortDirection.Input, false, id, name, valueTypeId, false, null,
                GraphAuthoringIdentity.Require(defaultFieldId, nameof(defaultFieldId))));

        public GraphNodePorts ValueOut(string id, string name, string valueTypeId) =>
            Add(new GraphNodePortDeclaration(GraphAuthoringPortDirection.Output, false, id, name, valueTypeId, false, null, null));

        GraphNodePorts Add(GraphNodePortDeclaration port)
        {
            if (ReferenceEquals(this, None))
                throw new InvalidOperationException("空端口表不可修改。");
            foreach (GraphNodePortDeclaration existing in m_Ports)
                if (existing.Id == port.Id && existing.Direction == port.Direction)
                    throw new InvalidOperationException($"端口 '{port.Id}' 重复声明。");
            m_Ports.Add(port);
            return this;
        }
    }
}
