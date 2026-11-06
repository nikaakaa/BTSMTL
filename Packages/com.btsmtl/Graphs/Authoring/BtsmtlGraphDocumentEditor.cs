#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Compilation;
using FlowCanvas;
using FlowCanvas.Macros;
using NodeCanvas.Framework;
using Newtonsoft.Json.Linq;
using ParadoxNotion;
using UnityEngine;
using CanvasGroup = NodeCanvas.Framework.CanvasGroup;

namespace BTSMTL.Graphs
{
    public sealed class BtsmtlGraphDocumentEditor
    {
        readonly FlowGraph m_Graph;
        readonly Action<string, IReadOnlyList<FlowGraph>, Action> m_Mutate;
        readonly Func<FlowGraph, BtsmtlGraphDocumentNode, Vector2, FlowNode> m_CreateNode;
        readonly Func<FlowNode, JObject> m_WriteNode;
        readonly Func<BtsmtlGraphDocument, BtsmtlGraphDocumentNode, object> m_ReadDefinition;
        readonly Func<object, Type, JToken> m_WriteValue;
        readonly Func<string, FlowGraph> m_GraphById;
        readonly Func<IReadOnlyList<BtsmtlGraphDocument>> m_Documents;
        readonly Func<IReadOnlyList<UnityEngine.Object>> m_References;

        public BtsmtlGraphDocumentEditor(FlowGraph graph, BtsmtlGraphDocument source, string sourcePath, UnityEngine.Object undoTarget,
            Action<string, IReadOnlyList<FlowGraph>, Action> mutate,
            Func<FlowGraph, BtsmtlGraphDocumentNode, Vector2, FlowNode> createNode,
            Func<FlowNode, JObject> writeNode,
            Func<BtsmtlGraphDocument, BtsmtlGraphDocumentNode, object> readDefinition,
            Func<JToken, Type, object> readValue, Func<object, Type, JToken> writeValue,
            Func<string, FlowGraph> graphById, Func<IReadOnlyList<BtsmtlGraphDocument>> documents,
            Func<IReadOnlyList<UnityEngine.Object>> references)
        {
            m_Graph = graph;
            Source = source;
            SourcePath = sourcePath;
            UndoTarget = undoTarget;
            m_Mutate = mutate;
            m_CreateNode = createNode;
            m_WriteNode = writeNode;
            m_ReadDefinition = readDefinition;
            ReadValue = readValue;
            m_WriteValue = writeValue;
            m_GraphById = graphById;
            m_Documents = documents;
            m_References = references;
        }

        public BtsmtlGraphDocument Source { get; private set; }
        public string SourcePath { get; }
        public UnityEngine.Object UndoTarget { get; }
        public Func<JToken, Type, object> ReadValue { get; }
        public IReadOnlyList<BtsmtlGraphDocument> Documents => m_Documents()
            .Where(document => document.AuthoringId != Source.AuthoringId).Prepend(Source).ToArray();

        public BtsmtlGraphDocument Document(string id) => Documents.Single(document => document.AuthoringId == id);
        public FlowGraph Graph(string id) => id == Source.AuthoringId ? m_Graph : m_GraphById(id);
        public object Definition(BtsmtlGraphDocument graph, BtsmtlGraphDocumentNode node) =>
            m_ReadDefinition(graph, node);
        public IReadOnlyList<UnityEngine.Object> References => Source.Nodes.Where(node => node.CalleeId != null)
            .Select(node => (UnityEngine.Object)Graph(node.CalleeId)).Concat(m_References()).Distinct().ToArray();

        public void Edit(string title, IReadOnlyList<FlowGraph> affectedGraphs, Action mutation) => m_Mutate(title, affectedGraphs, () =>
        {
            mutation();
            foreach (FlowGraph graph in affectedGraphs)
            {
                graph.UpdateNodeIDs(true);
                ((IBtsmtlAuthoringGraph)graph).Authoring.Document.Capture();
            }
        });

        public void Capture()
        {
            JObject root = Source.Json();
            root["name"] = m_Graph.name;
            root["allNodes"] = new JArray(m_Graph.allNodes.Cast<FlowNode>().Select(node =>
            {
                JObject value = m_WriteNode(node);
                value["UID"] = node.UID;
                value["_name"] = node.GetNameOverride();
                value["_tag"] = node.tag;
                value["outConnections"] = new JArray(node.outConnections.Cast<BinderConnection>().Select(edge => new JObject
                {
                    ["UID"] = edge.UID, ["targetNode"] = edge.targetNode.UID,
                    ["sourcePortID"] = edge.sourcePortID, ["targetPortID"] = edge.targetPortID
                }));
                value["_inputPortValues"] = new JObject(node.GetInputValuePorts().Select(input =>
                    new JProperty(input.ID, m_WriteValue(input.serializedValue, input.type))));
                if (node is MacroNodeWrapper call)
                    value["_macro"] = ((BtsmtlMacroGraph)call.macro).AuthoringId;
                return value;
            }));
            root["layout"] = new JObject
            {
                ["category"] = m_Graph.category, ["comments"] = m_Graph.comments,
                ["translation"] = m_WriteValue(m_Graph.translation, typeof(Vector2)),
                ["zoomFactor"] = m_Graph.zoomFactor,
                ["canvasGroups"] = m_WriteValue(m_Graph.canvasGroups, typeof(List<CanvasGroup>)),
                ["allNodes"] = new JObject(m_Graph.allNodes.Select(node => new JProperty(node.UID, new JObject
                {
                    ["position"] = m_WriteValue(node.position, typeof(Vector2)), ["comments"] = node.comments,
                    ["isBreakpoint"] = node.isBreakpoint
                })))
            };
            if (m_Graph is BtsmtlMacroGraph macro)
            {
                root["Domains"] = new JArray(((BtsmtlGraphMacroHost)macro.Host).Domains);
                root["inputDefinitions"] = Parameters(macro.inputDefinitions);
                root["outputDefinitions"] = Parameters(macro.outputDefinitions);
            }
            Source.Replace(root);
        }

        public void Restore(BtsmtlGraphDocument source)
        {
            Source = source;
            if (m_Graph is BtsmtlGraphRoot root)
                root.BindSource(source, root.VariableOwner);
            else
                ((BtsmtlMacroGraph)m_Graph).BindSource(source);
            var authoring = ((IBtsmtlAuthoringGraph)m_Graph).Authoring;
            authoring.BindDocument(this);
            authoring.Initialize(() =>
            {
                m_Graph.name = source.Name;
                m_Graph.allNodes.Clear();
                m_Graph.primeNode = null;
                JObject layout = (JObject)source.Json()["layout"];
                if (m_Graph is BtsmtlMacroGraph macro)
                {
                    macro.inputDefinitions.Clear();
                    macro.inputDefinitions.AddRange(source.Inputs.Select(Parameter));
                    macro.outputDefinitions.Clear();
                    macro.outputDefinitions.AddRange(source.Outputs.Select(Parameter));
                }
                foreach (BtsmtlGraphDocumentNode stored in source.Nodes)
                {
                    JObject nodeLayout = (JObject)layout["allNodes"][stored.Id];
                    var position = (Vector2)ReadValue(nodeLayout["position"], typeof(Vector2));
                    FlowNode node = m_CreateNode(m_Graph, stored, position);
                    node.ConfigureAuthoringIdentity(stored.Id);
                    node.name = stored.Name;
                    node.tag = (string)stored.Json()["_tag"];
                    node.comments = (string)nodeLayout["comments"];
                    node.isBreakpoint = (bool)nodeLayout["isBreakpoint"];
                    m_Graph.allNodes.Add(node);
                }
                foreach (BtsmtlGraphDocumentNode stored in source.Nodes)
                {
                    var node = (FlowNode)m_Graph.allNodes.Single(value => value.UID == stored.Id);
                    foreach (JProperty input in stored.InputValues().Properties())
                    {
                        var port = (ValueInput)node.GetInputPort(input.Name);
                        port.serializedValue = ReadValue(input.Value, port.type);
                    }
                }
                foreach (BtsmtlGraphDocumentNode stored in source.Nodes)
                    foreach (JObject edge in stored.OutConnections)
                    {
                        var from = (FlowNode)m_Graph.allNodes.Single(node => node.UID == stored.Id);
                        var to = (FlowNode)m_Graph.allNodes.Single(node => node.UID == (string)edge["targetNode"]);
                        BinderConnection connection = BinderConnection.CreateValidatedForDomain(
                            from.GetOutputPort((string)edge["sourcePortID"]), to.GetInputPort((string)edge["targetPortID"]), null);
                        connection.ConfigureAuthoringIdentity((string)edge["UID"]);
                    }
                m_Graph.category = (string)layout["category"];
                m_Graph.comments = (string)layout["comments"];
                m_Graph.translation = (Vector2)ReadValue(layout["translation"], typeof(Vector2));
                m_Graph.zoomFactor = (float)layout["zoomFactor"];
                m_Graph.canvasGroups = (List<CanvasGroup>)ReadValue(layout["canvasGroups"], typeof(List<CanvasGroup>));
            });
        }

        static JArray Parameters(IEnumerable<DynamicParameterDefinition> parameters) => new(parameters.Select(parameter =>
            new JObject
            {
                ["ID"] = parameter.ID, ["name"] = parameter.name,
                ["type"] = parameter.type == typeof(Flow) ? "Flow" : BtsmtlGraphVariableUnity.TypeId(parameter.type)
            }));

        static DynamicParameterDefinition Parameter(GraphNodePort port) => new(port.Id, port.DisplayName,
            port.IsFlow ? typeof(Flow) : BtsmtlGraphVariableUnity.Type(CompileValueTypes.FromId(port.ValueTypeId)));

        public static JObject WriteDeclaredNode(FlowNode node, Func<Type, string> typeName,
            Func<object, Type, JToken> writeValue)
        {
            var result = new JObject();
            foreach (GraphAuthoringMember member in GraphAuthoringMemberBindings.Contract(node).Fields)
                Store(result, member.SerializedMemberPath, writeValue(member.Read(node), member.ValueType));
            result["type"] = BtsmtlNativeValueNodes.TryGetNode(node.GetType(), out BtsmtlNativeValueNodeDefinition native)
                ? native.NodeTypeName : typeName(node.GetType());
            return result;
        }

        public static FlowNode CreateDeclaredNode(FlowGraph graph, BtsmtlGraphDocumentNode source, Vector2 position,
            Func<string, Type> resolveType, Func<JToken, Type, object> readValue, Func<string, FlowGraph> resolveGraph)
        {
            Type type = BtsmtlNativeValueNodeCatalog.TryGet(source.TypeName, out BtsmtlNativeValueNodeDefinition native)
                ? typeof(FlowCanvas.Nodes.SimplexNodeWrapper<>).MakeGenericType(BtsmtlNativeValueNodes.SourceType(native)) : resolveType(source.TypeName);
            return (FlowNode)Node.Create(graph, type, position, node =>
            {
                if (node is MacroNodeWrapper call)
                    call.macro = (Macro)resolveGraph(source.CalleeId);
                GraphAuthoringTypeContract contract = GraphAuthoringMemberBindings.Contract(node);
                GraphAuthoringCandidate candidate = contract.CreateCandidate(node, complete: true);
                JObject json = source.Json();
                foreach (GraphAuthoringMember member in contract.Fields)
                {
                    if (member.SerializedMemberPath.Count == 0)
                        continue;
                    JToken value = json;
                    foreach (string part in member.SerializedMemberPath)
                        value = ((JObject)value).TryGetValue(part, out JToken stored) ? stored :
                            throw new InvalidDataException($"节点 '{source.Id}' 缺少正式作者字段 '{part}'。");
                    member.WriteCandidate(candidate, readValue(value, member.ValueType));
                }
                candidate.Apply(node);
            });
        }

        static void Store(JObject root, IReadOnlyList<string> path, JToken value)
        {
            if (path.Count == 0)
                return;
            JObject owner = root;
            for (int index = 0; index + 1 < path.Count; index++)
            {
                if (owner[path[index]] is not JObject nested)
                    owner[path[index]] = nested = new JObject();
                owner = nested;
            }
            owner[path[path.Count - 1]] = value;
        }
    }
}
#endif
