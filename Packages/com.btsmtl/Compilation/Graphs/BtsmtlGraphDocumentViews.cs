using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Graphs;
using BTSMTL.Orchestration;
using Newtonsoft.Json.Linq;
using ThirdPersonSimulation;

namespace BTSMTL.Compilation
{
    public sealed class BtsmtlGraphDocumentViews
    {
        readonly Func<string, BtsmtlGraphDocument> m_Document;
        readonly Func<BtsmtlGraphDocument, BtsmtlGraphDocumentNode, object> m_Definition;
        readonly Func<BtsmtlGraphDocumentNode, object, GraphNodeFields> m_Fields;
        readonly Func<GraphNodeFields, GraphNodeCompileDescription> m_Describe;
        readonly Dictionary<string, CompileGraph> m_Graphs = new(StringComparer.Ordinal);
        readonly HashSet<string> m_Active = new(StringComparer.Ordinal);

        public BtsmtlGraphDocumentViews(Func<string, BtsmtlGraphDocument> document,
            Func<BtsmtlGraphDocument, BtsmtlGraphDocumentNode, object> definition,
            Func<BtsmtlGraphDocumentNode, object, GraphNodeFields> fields,
            Func<GraphNodeFields, GraphNodeCompileDescription> describe)
        {
            m_Document = document;
            m_Definition = definition;
            m_Fields = fields;
            m_Describe = describe;
        }

        public BtsmtlGraphDocument Document(string id) => m_Document(id);

        public CompileGraph Read(BtsmtlGraphDocument document)
        {
            if (m_Active.Contains(document.AuthoringId))
                throw new InvalidDataException($"子图 '{document.AuthoringId}' 递归调用自身。");
            if (m_Graphs.TryGetValue(document.AuthoringId, out CompileGraph existing))
                return existing;
            m_Active.Add(document.AuthoringId);
            try
            {
                if (document.IsMacro)
                    RequireInterface(document);
                var view = new CompileGraph(document.AuthoringId, SourceType(document),
                    document.Inputs.Select(Parameter).ToArray(), document.Outputs.Select(Parameter).ToArray());
                var connected = new HashSet<(string, string)>(document.Nodes.SelectMany(node => node.OutConnections)
                    .Select(edge => ((string)edge["targetNode"], (string)edge["targetPortID"])));
                var nodes = document.Nodes.Select(node => Node(document, node, connected)).ToArray();
                view.Complete(nodes, node => document.Node(node.Id).OutConnections.Select(edge =>
                    new CompileEdge((string)edge["UID"], "BTSMTL.Graphs.Connection", node,
                        (string)edge["sourcePortID"], (string)edge["targetNode"], (string)edge["targetPortID"],
                        node.ValueOutputs.Any(port => port.Id == (string)edge["sourcePortID"]))));
                foreach (CompileNode node in view.Nodes)
                    foreach (CompileEdge edge in node.OutEdges)
                        if (edge.IsValue
                                ? !node.ValueOutputs.Single(port => port.Id == edge.SourcePortId).Type
                                    .SameType(edge.Target.Input(edge.TargetPortId).Type)
                                : !node.FlowOutputs.Contains(edge.SourcePortId) || !edge.Target.FlowInputs.Contains(edge.TargetPortId))
                            throw new InvalidDataException($"连接 '{edge.Id}' 的端口或值类型不匹配。");
                m_Graphs.Add(document.AuthoringId, view);
                return view;
            }
            finally
            {
                m_Active.Remove(document.AuthoringId);
            }
        }

        CompileNode Node(BtsmtlGraphDocument document, BtsmtlGraphDocumentNode source,
            HashSet<(string, string)> connected)
        {
            BtsmtlNativeValueNodeCatalog.TryGet(source.TypeName, out BtsmtlNativeValueNodeDefinition native);
            CompileNodeRole role = source.TypeName switch
            {
                "BtsmtlMacroInputNode" => CompileNodeRole.MacroInput,
                "BtsmtlMacroOutputNode" => CompileNodeRole.MacroOutput,
                "MacroNodeWrapper" => CompileNodeRole.MacroCall,
                _ => CompileNodeRole.Leaf
            };
            object definition = native == null && role == CompileNodeRole.Leaf ? m_Definition(document, source) : null;
            if (definition != null && GraphNodeDomains.TryGet(definition.GetType(), out string domain) &&
                !document.Domains.Contains(domain, StringComparer.Ordinal))
                throw new InvalidDataException($"节点 '{source.Id}' 的领域 '{domain}' 不属于当前图。");
            if (definition is IBtsmtlVariableFlowNode)
                role = CompileNodeRole.Variable;
            BtsmtlGraphDocument callee = role == CompileNodeRole.MacroCall ? m_Document(source.CalleeId) : null;
            IReadOnlyList<GraphNodePort> ports = native != null ? native.Ports : role switch
            {
                CompileNodeRole.MacroCall => callee.Inputs.Concat(callee.Outputs).ToArray(),
                CompileNodeRole.MacroInput => Reverse(document.Inputs),
                CompileNodeRole.MacroOutput => Reverse(document.Outputs),
                _ => GraphNodePortShapes.Of(definition)
            };
            GraphNodeFields fields = definition == null ? null : m_Fields(source, definition);
            JObject values = source.InputValues();
            bool variable = definition is IBtsmtlVariableFlowNode;
            bool hostOutput = definition is IBtsmtlHostInputFlowNode &&
                GraphOrchestrationNodeDeclaration.Of(definition.GetType()).HostInputs == null;
            var node = new CompileNode(source.Id, definition?.GetType().FullName ?? source.TypeName,
                native?.Kind ?? source.TypeName, definition?.GetType(), role,
                ports.Where(port => port.IsInput && port.IsFlow).Select(port => port.Id).ToArray(),
                ports.Where(port => !port.IsInput && port.IsFlow).Select(port => port.Id).ToArray(),
                ports.Where(port => port.IsInput && !port.IsFlow).Select(port =>
                {
                    CompileValueType type = CompileValueTypes.FromId(port.ValueTypeId);
                    JToken value = values.TryGetValue(port.Id, out JToken stored) ? stored :
                        throw new InvalidDataException($"节点 '{source.Id}' 缺少端口 '{port.Id}' 的正式作者默认值。");
                    return new CompileValuePort(port.Id, native != null ? native.Input(port.Id) : variable ? "m_Value" : port.Id,
                        type, connected.Contains((source.Id, port.Id)), port.Required,
                        BtsmtlGraphVariableDeclarationsJson.ReadDefault(type, value));
                }).ToArray(),
                ports.Where(port => !port.IsInput && !port.IsFlow).Select(port => new CompileValuePort(port.Id,
                    native != null ? native.Output(port.Id) : variable || hostOutput ? "m_Output" : port.Id,
                    CompileValueTypes.FromId(port.ValueTypeId))).ToArray(), () => fields,
                _ => Describe(source, definition, native, fields));
            if (callee != null)
            {
                if (!callee.IsMacro || callee.Domains.Except(document.Domains, StringComparer.Ordinal).Any())
                    throw new InvalidDataException($"调用 '{source.Id}' 的子图类型或领域不属于调用方。");
                node.WithCallee(Read(callee));
            }
            if (definition is IBtsmtlVariableFlowNode reference)
                node.WithVariable(reference.OwnerId, reference.VariableId, reference.Writes);
            return node;
        }

        SimulationNodeEmission Describe(BtsmtlGraphDocumentNode source, object definition,
            BtsmtlNativeValueNodeDefinition native, GraphNodeFields fields)
        {
            if (native != null)
                return new SimulationNodeEmission(native.Code, integer0: native.Variant);
            if (BtsmtlGraphDomainNodes.TryDescribe(GraphOrchestrationNodeDeclaration.Of(definition.GetType()),
                    () => m_Describe(fields), out SimulationNodeEmission domain))
                return domain;
            if (definition is IBtsmtlHostInputFlowNode input)
            {
                CompileValueType type = CompileValueTypes.FromId(GraphNodeValueTypes.Of(input.ValueType));
                return new SimulationNodeEmission(OrchestrationOperationCode.HostInput,
                    hostInputs: new[] { new SemanticHostInputPort("m_Output", input.InputId, type.Kind,
                        type.IsEnum ? "Enum:" + type.EnumIdentity : type.Kind == SemanticValueKind.Number
                            ? nameof(OrchestrationValueKind.Scalar) : type.Kind.ToString()) });
            }
            if (definition is IBtsmtlVariableFlowNode variable)
                return new SimulationNodeEmission(variable.Writes ? OrchestrationOperationCode.BlackboardSet
                    : OrchestrationOperationCode.BlackboardGet, text0: variable.VariableId);
            var operation = (BtsmtlGraphOperationAttribute)Attribute.GetCustomAttribute(definition.GetType(),
                typeof(BtsmtlGraphOperationAttribute), false);
            if (operation == null)
                throw new InvalidDataException($"节点 '{source.Id}' 的类型 '{source.TypeName}' 没有编排操作声明。");
            return new SimulationNodeEmission(operation.Code,
                integer0: ((IBtsmtlGraphOperationSource)definition).OperationVariant);
        }

        static CompileGraphParameter Parameter(GraphNodePort port) => new(port.Id, port.DisplayName,
            port.IsFlow ? null : CompileValueTypes.FromId(port.ValueTypeId), port.IsFlow);

        static void RequireInterface(BtsmtlGraphDocument document)
        {
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (GraphNodePort port in document.Inputs.Concat(document.Outputs))
            {
                if (!identities.Add(port.Id))
                    throw new InvalidDataException($"子图 '{document.AuthoringId}' 的参数身份 '{port.Id}' 重复。");
                string name = (port.IsInput ? "input:" : "output:") + port.DisplayName;
                if (string.IsNullOrWhiteSpace(port.DisplayName) || !names.Add(name))
                    throw new InvalidDataException($"子图 '{document.AuthoringId}' 的参数名称必须非空且在同方向内唯一。");
            }
            if (document.Inputs.Count(port => port.IsFlow) != 1 || document.Outputs.Any(port => port.IsFlow))
                throw new InvalidDataException($"子图 '{document.AuthoringId}' 必须有唯一执行入口，完成时由调用返回。");
        }

        static IReadOnlyList<GraphNodePort> Reverse(IReadOnlyList<GraphNodePort> ports) => ports.Select(port =>
            new GraphNodePort(port.IsInput ? GraphAuthoringPortDirection.Output : GraphAuthoringPortDirection.Input,
                port.IsFlow, port.Id, port.DisplayName, port.ValueTypeId, port.Order)).ToArray();

        public static string SourceType(BtsmtlGraphDocument document) => document.IsMacro
            ? "BTSMTL.Graphs.BtsmtlMacroGraph" : "BTSMTL.Graphs.BtsmtlGraphRoot";
    }
}
