using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;

namespace BTSMTL.Authoring.Documents
{
    public readonly struct AuthoringFlowGraphAnchor
    {
        public AuthoringFlowGraphAnchor(string name, string identity, GraphAuthoringCapabilityId capabilityId)
        { Name = name; Identity = identity; CapabilityId = capabilityId; }
        public string Name { get; }
        public string Identity { get; }
        public GraphAuthoringCapabilityId CapabilityId { get; }
    }

    public readonly struct AuthoringFlowGraphNode
    {
        public AuthoringFlowGraphNode(GraphAuthoringElementId id, GraphAuthoringCapabilityId capabilityId,
            IReadOnlyList<GraphAuthoringDynamicPortProjection> ports)
        { Id = id; CapabilityId = capabilityId; Ports = ports; }
        public GraphAuthoringElementId Id { get; }
        public GraphAuthoringCapabilityId CapabilityId { get; }
        public IReadOnlyList<GraphAuthoringDynamicPortProjection> Ports { get; }
    }

    public static class AuthoringFlowGraphContract
    {
        public static void ReceiveDocument(AuthoringGraphDocument graph,
            IReadOnlyList<AuthoringFieldDefinition> graphFields, IReadOnlyList<AuthoringFieldDefinition> edgeFields,
            IReadOnlyDictionary<GraphAuthoringCapabilityId, AuthoringNodeDefinition> capabilities,
            IReadOnlyDictionary<string, AuthoringValueDefinition> inputTypes,
            IReadOnlyList<AuthoringFlowGraphAnchor> anchors, bool requireComplete)
        {
            string path = "graph:" + graph.Id;
            AuthoringFieldDefinition.ReceiveFields(graphFields, graph.Fields, path);
            foreach (AuthoringEdgeDocument edge in graph.Edges.Span)
                AuthoringFieldDefinition.ReceiveFields(edgeFields, edge.Fields, path + "/edge:" + edge.Id);
            var nodes = new List<AuthoringFlowGraphNode>();
            foreach (AuthoringNodeDocument node in graph.Nodes.Span)
            {
                if (!capabilities.TryGetValue(node.CapabilityId, out AuthoringNodeDefinition definition))
                    throw Error(path + "/node:" + node.Id, "原能力目录没有此节点能力。");
                nodes.Add(new AuthoringFlowGraphNode(node.Id, node.CapabilityId,
                    definition.Receive(node, graph.RoleId, inputTypes)));
            }
            Validate(path, nodes, graph.Edges.ToArray(), capabilities.Values.Where(definition =>
                definition.Roles.Contains(graph.RoleId)).Select(definition => definition.Id).ToHashSet(), anchors, requireComplete);
        }

        public static void Validate(string path, IReadOnlyList<AuthoringFlowGraphNode> nodes,
            IReadOnlyList<AuthoringEdgeDocument> edges, ISet<GraphAuthoringCapabilityId> allowedCapabilities,
            IReadOnlyList<AuthoringFlowGraphAnchor> anchors, bool requireComplete)
        {
            var byId = new Dictionary<GraphAuthoringElementId, AuthoringFlowGraphNode>();
            var ports = new Dictionary<(GraphAuthoringElementId, GraphAuthoringPortId), GraphAuthoringDynamicPortProjection>();
            var occupied = new HashSet<(GraphAuthoringElementId, GraphAuthoringPortId)>();
            var anchorKinds = new HashSet<GraphAuthoringCapabilityId>();
            var outgoing = new Dictionary<GraphAuthoringElementId, List<GraphAuthoringElementId>>();
            foreach (AuthoringFlowGraphNode node in nodes)
            {
                if (!allowedCapabilities.Contains(node.CapabilityId))
                    throw Error(path + "/node:" + node.Id, "节点不属于当前页面。");
                if (!byId.TryAdd(node.Id, node))
                    throw Error(path, "节点身份重复。");
                foreach (AuthoringFlowGraphAnchor anchor in anchors)
                    if (anchor.CapabilityId.Equals(node.CapabilityId) && !anchorKinds.Add(node.CapabilityId))
                        throw Error(path, "系统入口重复。");
                foreach (GraphAuthoringDynamicPortProjection port in node.Ports)
                    ports.Add((node.Id, port.PortId), port);
                outgoing.Add(node.Id, new List<GraphAuthoringElementId>());
            }
            var identities = new HashSet<GraphAuthoringElementId>();
            foreach (AuthoringEdgeDocument edge in edges)
            {
                string edgePath = path + "/edge:" + edge.Id;
                if (!identities.Add(edge.Id) || !byId.ContainsKey(edge.SourceNodeId) || !byId.ContainsKey(edge.TargetNodeId))
                    throw Error(edgePath, "连线身份或端点归属无效。");
                var sourceKey = (edge.SourceNodeId, edge.SourcePortId);
                var targetKey = (edge.TargetNodeId, edge.TargetPortId);
                if (!ports.TryGetValue(sourceKey, out GraphAuthoringDynamicPortProjection source) ||
                    !ports.TryGetValue(targetKey, out GraphAuthoringDynamicPortProjection target) ||
                    source.Direction != GraphAuthoringPortDirection.Output ||
                    target.Direction != GraphAuthoringPortDirection.Input || source.ValueTypeId != target.ValueTypeId)
                    throw Error(edgePath, "端口缺失、方向或声明类型不一致。");
                if (source.Capacity == GraphAuthoringPortCapacity.Single && !occupied.Add(sourceKey) ||
                    target.Capacity == GraphAuthoringPortCapacity.Single && !occupied.Add(targetKey))
                    throw Error(edgePath, "端口超过正式连接容量。");
                outgoing[edge.SourceNodeId].Add(edge.TargetNodeId);
            }
            var active = new HashSet<GraphAuthoringElementId>();
            var visited = new HashSet<GraphAuthoringElementId>();
            foreach (AuthoringFlowGraphNode node in nodes)
                Visit(node.Id, path, outgoing, active, visited);
            if (!requireComplete)
                return;
            foreach (AuthoringFlowGraphAnchor anchor in anchors)
                if (!byId.TryGetValue(new GraphAuthoringElementId(anchor.Identity), out AuthoringFlowGraphNode node) ||
                    !node.CapabilityId.Equals(anchor.CapabilityId))
                    throw Error(path, $"页面缺少固定身份 '{anchor.Identity}' 的 {anchor.Name} 系统入口。");
        }

        static void Visit(GraphAuthoringElementId node, string path,
            IReadOnlyDictionary<GraphAuthoringElementId, List<GraphAuthoringElementId>> outgoing,
            ISet<GraphAuthoringElementId> active, ISet<GraphAuthoringElementId> visited)
        {
            if (visited.Contains(node))
                return;
            if (!active.Add(node))
                throw Error(path + "/node:" + node, "连线形成环；重复执行应使用循环节点。");
            foreach (GraphAuthoringElementId target in outgoing[node])
                Visit(target, path, outgoing, active, visited);
            active.Remove(node);
            visited.Add(node);
        }

        static InvalidOperationException Error(string path, string message) => new(path + ": " + message);
    }
}
