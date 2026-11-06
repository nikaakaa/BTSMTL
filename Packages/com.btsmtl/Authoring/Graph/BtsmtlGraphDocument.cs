using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using Newtonsoft.Json.Linq;

namespace BTSMTL.Authoring.Graph
{
    public sealed class BtsmtlGraphDocument
    {
        JObject m_Root;

        internal BtsmtlGraphDocument(JObject root)
        {
            Require(root);
            m_Root = (JObject)root.DeepClone();
        }

        public string AuthoringId => (string)m_Root["AuthoringId"];
        public string ContentRevision => (string)m_Root["ContentRevision"];
        public string Name => (string)m_Root["name"];
        public string ContentDomain => (string)m_Root["m_ContentDomain"];
        public bool IsMacro => ContentDomain == "macro";
        public string Ownership => IsMacro ? (string)m_Root["Ownership"] : null;
        public string VariableOwnerId => (string)m_Root["VariableOwnerId"];
        public IReadOnlyList<string> Domains => m_Root["Domains"].Values<string>().ToArray();
        public IReadOnlyList<BtsmtlGraphEntryDeclaration> Entries => ((JArray)m_Root["EntryDeclarations"])
            .Cast<JObject>().Select(value => new BtsmtlGraphEntryDeclaration((string)value["m_EntryId"],
                (string)value["m_NodeId"], (OrchestrationExecutionContract)(int)value["m_Contract"])).ToArray();
        public IReadOnlyList<BtsmtlGraphDocumentNode> Nodes => ((JArray)m_Root["allNodes"])
            .Cast<JObject>().Select(value => new BtsmtlGraphDocumentNode(value)).ToArray();
        public IReadOnlyList<GraphNodePort> Inputs => Parameters("inputDefinitions", GraphAuthoringPortDirection.Input);
        public IReadOnlyList<GraphNodePort> Outputs => Parameters("outputDefinitions", GraphAuthoringPortDirection.Output);
        public JObject Json() => (JObject)m_Root.DeepClone();

        public BtsmtlGraphDocumentNode Node(string id) => Nodes.Single(node => node.Id == id);

        public void Replace(JObject root)
        {
            Require(root);
            if (Same(m_Root, root))
                return;
            string revision = Same(BtsmtlGraphDocumentJson.CompilationGraph(m_Root),
                BtsmtlGraphDocumentJson.CompilationGraph(root)) ? ContentRevision : Guid.NewGuid().ToString("N");
            m_Root = (JObject)root.DeepClone();
            m_Root["ContentRevision"] = revision;
        }

        public void ConfigureIdentity(string id, string revision)
        {
            m_Root["AuthoringId"] = GraphAuthoringIdentity.Require(id, nameof(id));
            m_Root["ContentRevision"] = GraphAuthoringIdentity.Require(revision, nameof(revision));
        }

        public void ConfigureEntries(IEnumerable<BtsmtlGraphEntryDeclaration> entries) =>
            Configure("EntryDeclarations", new JArray(entries.Select(entry => new JObject
            {
                ["m_EntryId"] = entry.EntryId, ["m_NodeId"] = entry.NodeId, ["m_Contract"] = (int)entry.Contract
            })));

        public void ConfigureDomains(IEnumerable<string> domains) => Configure("Domains", new JArray(domains));
        public void ConfigureVariableOwner(string ownerId) =>
            Configure("VariableOwnerId", new JValue(GraphAuthoringIdentity.Require(ownerId, nameof(ownerId))));

        void Configure(string member, JToken value)
        {
            if (Same(m_Root[member], value))
                return;
            m_Root[member] = value;
            m_Root["ContentRevision"] = Guid.NewGuid().ToString("N");
        }

        IReadOnlyList<GraphNodePort> Parameters(string member, GraphAuthoringPortDirection direction) =>
            !IsMacro ? Array.Empty<GraphNodePort>() : m_Root[member].Select((parameter, order) =>
                new GraphNodePort(direction, (string)parameter["type"] == "Flow", (string)parameter["ID"],
                    (string)parameter["name"], (string)parameter["type"], order)).ToArray();

        static void Require(JObject root)
        {
            string domain = (string)Required(root, "m_ContentDomain");
            if (domain != "graph" && domain != "macro")
                throw new InvalidDataException($"通用图文档领域必须是 graph 或 macro，实际为 '{domain}'。");
            GraphAuthoringIdentity.Require((string)Required(root, "AuthoringId"), "AuthoringId");
            GraphAuthoringIdentity.Require((string)Required(root, "ContentRevision"), "ContentRevision");
            _ = Required(root, "name");
            _ = (JArray)Required(root, "Domains");
            _ = (JArray)Required(root, "EntryDeclarations");
            _ = (JObject)Required(root, "layout");
            string owner = (string)Required(root, "VariableOwnerId");
            GraphAuthoringIdentity.Require(owner, "VariableOwnerId");
            var nodes = ((JArray)Required(root, "allNodes")).Cast<JObject>().ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var edges = new HashSet<string>(StringComparer.Ordinal);
            foreach (JObject node in nodes)
            {
                string id = GraphAuthoringIdentity.Require((string)Required(node, "UID"), "UID");
                if (!ids.Add(id))
                    throw new InvalidDataException($"图节点身份 '{id}' 重复。");
                GraphAuthoringIdentity.Require((string)Required(node, "type"), "type");
                _ = (JObject)Required(node, "_inputPortValues");
                foreach (JObject edge in (JArray)Required(node, "outConnections"))
                {
                    string edgeId = GraphAuthoringIdentity.Require((string)Required(edge, "UID"), "UID");
                    if (!edges.Add(edgeId))
                        throw new InvalidDataException($"图连接身份 '{edgeId}' 重复。");
                    _ = Required(edge, "targetNode");
                    _ = Required(edge, "sourcePortID");
                    _ = Required(edge, "targetPortID");
                }
            }
            foreach (JObject edge in nodes.SelectMany(node => (JArray)node["outConnections"]))
                if (!ids.Contains((string)edge["targetNode"]))
                    throw new InvalidDataException($"连接 '{edge["UID"]}' 指向不存在的节点 '{edge["targetNode"]}'。");
            if (domain != "macro")
                return;
            string ownership = (string)Required(root, "Ownership");
            if (ownership != "Private" && ownership != "Shared")
                throw new InvalidDataException("子图 Ownership 必须明确声明 Private 或 Shared。");
            _ = (JArray)Required(root, "inputDefinitions");
            _ = (JArray)Required(root, "outputDefinitions");
        }

        internal static JToken Required(JObject owner, string member) => owner.TryGetValue(member, out JToken value)
            ? value : throw new InvalidDataException($"作者字段 '{owner.Path}.{member}' 缺失。");

        static bool Same(JToken left, JToken right)
        {
            if (left.Type != right.Type)
                return false;
            if (left is JObject a && right is JObject b)
                return a.Count == b.Count && a.Properties().All(property => b.TryGetValue(property.Name, out JToken value)
                    && Same(property.Value, value));
            if (left is JArray x && right is JArray y)
                return x.Count == y.Count && x.Zip(y, Same).All(equal => equal);
            if (left.Type == JTokenType.Float)
                return BitConverter.DoubleToInt64Bits((double)left) == BitConverter.DoubleToInt64Bits((double)right);
            return JToken.DeepEquals(left, right);
        }
    }

    public sealed class BtsmtlGraphDocumentNode
    {
        readonly JObject m_Value;
        internal BtsmtlGraphDocumentNode(JObject value) => m_Value = value;
        public string Id => (string)m_Value["UID"];
        public string TypeName => (string)m_Value["type"];
        public string Name => (string)m_Value["_name"];
        public string CalleeId => (string)m_Value["_macro"];
        public JObject Json() => (JObject)m_Value.DeepClone();
        public JObject InputValues() => (JObject)m_Value["_inputPortValues"].DeepClone();
        public IReadOnlyList<JObject> OutConnections => ((JArray)m_Value["outConnections"])
            .Select(value => (JObject)value.DeepClone()).ToArray();
    }

    public static class BtsmtlGraphDocumentJson
    {
        public static BtsmtlGraphDocument Read(JObject root) => new(root);
        public static JObject Write(BtsmtlGraphDocument source) => source.Json();
        public static string CompilationInput(BtsmtlGraphDocument source, BtsmtlGraphVariableDeclarations variables)
        {
            return new JObject { ["graph"] = CompilationGraph(source.Json()),
                ["variables"] = BtsmtlGraphVariableDeclarationsJson.Write(variables) }
                .ToString(Newtonsoft.Json.Formatting.None);
        }

        internal static JObject CompilationGraph(JObject source)
        {
            var graph = (JObject)source.DeepClone();
            graph.Remove("layout");
            graph.Remove("ContentRevision");
            return graph;
        }
    }
}
