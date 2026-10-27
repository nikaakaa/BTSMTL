using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public sealed class AuthoringNodeDefinition
    {
        public AuthoringNodeDefinition(GraphAuthoringCapabilityId id, GraphAuthoringDomainId domain,
            IReadOnlyList<GraphAuthoringDocumentRoleId> roles, IReadOnlyList<AuthoringFieldDefinition> fields,
            GraphAuthoringPortShapeDefinition portShape, GraphAuthoringDynamicPortSource dynamicPortSource,
            bool systemOwned, string anchorId, AuthoringStateKind? stateKind)
        {
            Id = id; Domain = domain; Roles = roles; Fields = fields.ToDictionary(field => field.Id);
            PortShape = portShape; DynamicPortSource = dynamicPortSource;
            SystemOwned = systemOwned; AnchorId = anchorId; StateKind = stateKind;
        }
        public GraphAuthoringCapabilityId Id { get; }
        public GraphAuthoringDomainId Domain { get; }
        public IReadOnlyList<GraphAuthoringDocumentRoleId> Roles { get; }
        public IReadOnlyDictionary<GraphAuthoringFieldId, AuthoringFieldDefinition> Fields { get; }
        public GraphAuthoringPortShapeDefinition PortShape { get; }
        public GraphAuthoringDynamicPortSource DynamicPortSource { get; }
        public bool SystemOwned { get; }
        public string AnchorId { get; }
        public AuthoringStateKind? StateKind { get; }

        public IReadOnlyDictionary<GraphAuthoringFieldId, AuthoringValue> EffectiveFields(AuthoringNodeDocument node)
        {
            var fields = Fields.ToDictionary(field => field.Key, field => field.Value.DefaultValue);
            var authored = new HashSet<GraphAuthoringFieldId>();
            foreach (AuthoringNamedValue field in node.Fields.Span)
            {
                if (!authored.Add(field.FieldId))
                    throw Error(node, field.FieldId.Value, "作者字段重复。");
                if (field.FieldId.Value.StartsWith("input:", StringComparison.Ordinal)) continue;
                if (!Fields.ContainsKey(field.FieldId))
                    throw Error(node, field.FieldId.Value, "字段不属于原节点定义。");
                fields[field.FieldId] = field.Value;
            }
            return fields;
        }

        public IReadOnlyList<GraphAuthoringDynamicPortProjection> Receive(AuthoringNodeDocument node,
            GraphAuthoringDocumentRoleId role, IReadOnlyDictionary<string, AuthoringValueDefinition> inputTypes)
        {
            if (!node.CapabilityId.Equals(Id) || !Roles.Contains(role))
                throw Error(node, "capability", "节点能力不属于当前定义或图角色。");
            IReadOnlyDictionary<GraphAuthoringFieldId, AuthoringValue> fields = EffectiveFields(node);
            foreach (var field in fields) Fields[field.Key].Receive(field.Value, "node:" + node.Id + "/" + field.Key);
            IReadOnlyList<GraphAuthoringDynamicPortProjection> ports = ProjectPorts(node, fields);
            var inputs = ports.Where(port => port.Direction == GraphAuthoringPortDirection.Input)
                .ToDictionary(port => port.PortId.Value, StringComparer.Ordinal);
            foreach (AuthoringNamedValue field in node.Fields.Span)
            {
                string id = field.FieldId.Value;
                if (!id.StartsWith("input:", StringComparison.Ordinal)) continue;
                string portId = id.Substring("input:".Length);
                if (!inputs.TryGetValue(portId, out GraphAuthoringDynamicPortProjection port) ||
                    !inputTypes.TryGetValue(port.ValueTypeId, out AuthoringValueDefinition definition))
                    throw Error(node, id, "常量字段没有对应的正式值输入端口和类型定义。");
                definition.Receive(field.Value, "node:" + node.Id + "/" + id);
            }
            return ports;
        }

        public IReadOnlyList<GraphAuthoringDynamicPortProjection> ProjectPorts(AuthoringNodeDocument node) =>
            ProjectPorts(node, EffectiveFields(node));

        IReadOnlyList<GraphAuthoringDynamicPortProjection> ProjectPorts(AuthoringNodeDocument node,
            IReadOnlyDictionary<GraphAuthoringFieldId, AuthoringValue> fields)
        {
            var controls = PortShape.PortVariants.Select(variant => variant.When.FieldId).ToHashSet();
            controls.UnionWith(PortShape.FixedPorts.Concat(PortShape.PortVariants.SelectMany(variant => variant.Ports))
                .Select(port => port.ValueTypeFieldId).Where(field => field.IsValid));
            GraphAuthoringTypedPropertyValue[] properties = controls.Select(id => new GraphAuthoringTypedPropertyValue(
                id, Fields[id].ValueKind, ((AuthoringScalarValue)fields[id]).Value)).ToArray();
            GraphAuthoringDynamicPortProjection[] authored = node.DynamicPorts.ToArray().Select((port, order) =>
                new GraphAuthoringDynamicPortProjection(port.Id, port.DisplayName, port.ValueTypeId,
                    port.Direction, port.Capacity, false, order, port.InterfacePortId)).ToArray();
            return GraphAuthoringNodePortShapeProjector.ProjectComplete(PortShape, properties, authored);
        }

        static InvalidOperationException Error(AuthoringNodeDocument node, string field, string reason) =>
            new InvalidOperationException("node:" + node.Id + "/" + field + ": " + reason);
    }
}
