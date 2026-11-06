#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using FlowCanvas;
using FlowCanvas.Macros;
using FlowCanvas.Nodes;
using NodeCanvas.Framework;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Graphs
{
    // 通用图与子图共用的作者图入口；节点 Inspector、变量菜单和生成 API 只经此访问所属图。
    public interface IBtsmtlAuthoringGraph
    {
        FlowGraph Graph { get; }
        BtsmtlFlowGraphAuthoring Authoring { get; }
        BtsmtlGraphVariableDeclarations Variables { get; }
        BtsmtlGraphVariableEditor VariableEditor { get; }
        // 子图变量只属于调用位置的局部状态，不发布到宿主，也不改变黑板作用域。
        bool PublishesVariables { get; }
    }

    // 分组注释的作者声明：名称、范围、颜色、自动包围与说明。生成时整体替换图的分组列表；编译与指纹不读取分组。
    public sealed class BtsmtlCanvasGroupDeclaration
    {
        public BtsmtlCanvasGroupDeclaration(string name, Rect rect, Color color, bool autoGroup, string notes = "")
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("分组注释必须有名称。", nameof(name));
            Name = name;
            Rect = rect;
            Color = color;
            AutoGroup = autoGroup;
            Notes = notes ?? string.Empty;
        }

        public string Name { get; }
        public Rect Rect { get; }
        public Color Color { get; }
        public bool AutoGroup { get; }
        public string Notes { get; }

        // 所属图的编辑事务由调用方提供；技能页与状态机页在各自的生成事务内直接调用。
        public static void Apply(Graph graph, IEnumerable<BtsmtlCanvasGroupDeclaration> groups) =>
            graph.canvasGroups = groups.Select(group => new NodeCanvas.Framework.CanvasGroup(group.Rect, group.Name)
            {
                color = group.Color,
                autoGroup = group.AutoGroup,
                notes = group.Notes
            }).ToList();
    }

    // FlowCanvas 编辑入口的共同实现；通用图和子图因基类不同各持有一份，只转发不复制。
    public sealed class BtsmtlFlowGraphAuthoring
    {
        static int s_ConnectionValidationDepth;
        readonly FlowGraph m_Graph;
        int m_EditDepth;
        Func<string, BtsmtlMacroGraph> m_CreatePrivateMacro;
        BtsmtlGraphDocumentEditor m_Document;

        public BtsmtlFlowGraphAuthoring(FlowGraph graph) =>
            m_Graph = graph ? graph : throw new ArgumentNullException(nameof(graph));

        public void BindPrivateMacroCreation(Func<string, BtsmtlMacroGraph> create) => m_CreatePrivateMacro = create;
        public void BindDocument(BtsmtlGraphDocumentEditor document) => m_Document = document;
        public BtsmtlGraphDocumentEditor Document => m_Document ??
            throw new InvalidOperationException("通用图尚未绑定正式作者文档。");

        public BtsmtlMacroGraph CreatePrivateMacro(string name) =>
            (m_CreatePrivateMacro ?? throw new InvalidOperationException("所属作者文档尚未绑定私有子图创建入口。"))(name);

        bool UsesGraphDocument => m_Graph is BtsmtlGraphRoot ||
            m_Graph is BtsmtlMacroGraph { Host: BtsmtlGraphMacroHost };

        public UnityEngine.Object UndoTarget => m_EditDepth != 0 ? null :
            UsesGraphDocument ? Document.UndoTarget : m_Graph;

        BtsmtlMacroHost MacroHost => m_Graph is BtsmtlMacroGraph macro ? macro.Host :
            m_Graph is IBtsmtlMacroHostProvider provider ? provider.CreatePrivateMacroHost() : null;

        public void Edit(string title, Action mutation)
        {
            if (m_EditDepth != 0)
            {
                mutation();
                return;
            }
            Edit(title, new[] { m_Graph }, mutation);
        }

        internal void Edit(string title, FlowGraph[] affectedGraphs, Action mutation)
        {
            m_EditDepth++;
            try
            {
                if (UsesGraphDocument)
                    Document.Edit(title, affectedGraphs, mutation);
                else
                    MacroHost.Edit(m_Graph, title, affectedGraphs, mutation);
            }
            finally
            {
                m_EditDepth--;
            }
        }

        public void Initialize(Action mutation)
        {
            m_EditDepth++;
            try
            {
                mutation();
                m_Graph.UpdateNodeIDs(true);
                m_Graph.SelfSerialize();
            }
            finally
            {
                m_EditDepth--;
            }
        }

        public bool CanConnect(Port source, Port target, out string reason)
        {
            reason = null;
            if (source.parent.graph != m_Graph || target.parent.graph != m_Graph)
                reason = "图连接的两端必须属于当前图。";
            else if (source.type != target.type || source.IsFlowPort() != target.IsFlowPort())
                reason = "图连接必须使用相同类型和种类的端口。";
            else if (source is FlowOutput && source.connections != 0)
                reason = "执行输出端口已连接。";
            else if (target is ValueInput && target.connections != 0)
                reason = "值输入端口已连接。";
            else if (s_ConnectionValidationDepth == 0)
            {
                s_ConnectionValidationDepth++;
                try { return BinderConnection.CanBeBoundVerbosed(source, target, null, out reason); }
                finally { s_ConnectionValidationDepth--; }
            }
            return reason == null;
        }

        public Node AddNode(Type nodeType, Func<Node> add)
        {
            if (!m_Graph.CanAuthorNodeType(nodeType))
                throw new InvalidOperationException($"节点 '{nodeType.FullName}' 不属于当前图的作者合同。");
            Node created = null;
            Edit("创建图节点", () =>
            {
                created = add();
                _ = created.UID;
            });
            return created;
        }

        public BinderConnection Connect(Port source, Port target)
        {
            if (!CanConnect(source, target, out string reason))
                throw new InvalidOperationException(reason);
            BinderConnection connection = null;
            Edit("连接图端口", () => connection = BinderConnection.CreateValidatedForDomain(source, target, null));
            return connection;
        }

        public void Disconnect(Port port, Action<Connection> remove) => Edit("断开图端口", () =>
        {
            foreach (Connection connection in port.GetPortConnections().ToArray())
                remove(connection);
        });

        public void Clear(Action<Node> remove) => Edit("清理图节点", () =>
        {
            foreach (Node node in m_Graph.allNodes.ToArray())
                remove(node);
            m_Graph.canvasGroups = new List<NodeCanvas.Framework.CanvasGroup>();
        });

        public GenericMenu NodesMenu(Vector2 position, Port context, UnityEngine.Object instance)
        {
            GenericMenu menu = m_Graph.AppendFlowNodesMenu(new GenericMenu(), string.Empty, position, context, instance);
            foreach (BtsmtlNativeValueNodeDefinition definition in BtsmtlNativeValueNodeCatalog.All)
            {
                Type type = typeof(SimplexNodeWrapper<>).MakeGenericType(BtsmtlNativeValueNodes.SourceType(definition));
                if (m_Graph.CanAuthorNodeType(type))
                    AppendNodeCreationItem(menu, "数值/" + definition.Kind, type, position, context);
            }
            AppendMacroItems(menu, position, context);
            return menu;
        }

        // 子图入口：新建私有子图取本图准入；共享子图只列出其全部节点都被本图接受的资产。
        public void AppendMacroItems(GenericMenu menu, Vector2 position, Port context)
        {
            if (m_Graph.isEditorReadOnly || !m_Graph.CanAuthorNodeType(typeof(MacroNodeWrapper)) ||
                m_Graph is not IBtsmtlMacroHostProvider provider || context != null && context is not FlowOutput)
                return;
            BtsmtlMacroHost host = provider.CreatePrivateMacroHost();
            menu.AddItem(new GUIContent("子图/新建私有子图"), false, () => Run(() => Edit("创建私有子图", () =>
            {
                BtsmtlMacroGraph macro = host.CreatePrivateMacro(m_Graph, m_Graph.name + " 子图");
                AddCall(macro, position, context);
            })));
            foreach (BtsmtlMacroGraph macro in host.SharedMacros(m_Graph))
            {
                if (macro == m_Graph || !macro.allNodes.All(node =>
                        node is BtsmtlMacroInputNode || node is BtsmtlMacroOutputNode ||
                        m_Graph.CanAuthorNodeType(macro.Host.AuthoringNodeType(node))))
                    continue;
                menu.AddItem(new GUIContent("子图/调用共享子图/" + host.SharedMacroLabel(macro)), false,
                    () => Run(() => Edit("调用共享子图", () => AddCall(macro, position, context))));
            }
        }

        public MacroNodeWrapper AddCall(BtsmtlMacroGraph macro, Vector2 position, Port context)
        {
            MacroNodeWrapper call = null;
            Edit("调用子图", () =>
            {
                call = (MacroNodeWrapper)m_Graph.AddNode(typeof(MacroNodeWrapper), position);
                call.macro = macro;
                if (context != null)
                    ConnectCreated(context, call, macro.inputDefinitions.Single(value => value.type == typeof(Flow)).ID);
            });
            return call;
        }

        static void Run(Action action)
        {
            try { action(); }
            catch (InvalidOperationException error)
            {
                NodeCanvas.Editor.GraphEditor.current?.ShowNotification(new GUIContent(error.Message));
            }
        }

        public void AppendNodeCreationItem(GenericMenu menu, string category, Type type, Vector2 position, Port context)
        {
            Type resolved = type.IsGenericTypeDefinition
                ? type.MakeGenericType(context != null && context.IsValuePort() ? context.type : typeof(float))
                : type;
            if (context == null)
            {
                menu.AddItem(new GUIContent(category), false, () => m_Graph.AddNode(resolved, position));
                return;
            }
            var prototype = (FlowNode)Activator.CreateInstance(resolved);
            prototype.GatherPorts();
            Port[] ports = (context.IsOutputPort()
                    ? prototype.GetInputFlowPorts().Cast<Port>().Concat(prototype.GetInputValuePorts())
                    : prototype.GetOutputFlowPorts().Cast<Port>().Concat(prototype.GetOutputValuePorts()))
                .Where(port => port.type == context.type && port.IsFlowPort() == context.IsFlowPort()).ToArray();
            foreach (Port candidate in ports)
                menu.AddItem(new GUIContent(ports.Length == 1 ? category : category + "/" + candidate.name), false,
                    () => Edit("创建并连接图节点", () =>
                    {
                        var node = (FlowNode)m_Graph.AddNode(resolved, position);
                        ConnectCreated(context, node, candidate.ID);
                    }));
        }

        public void ConnectCreated(Port context, FlowNode node, string portId)
        {
            Port endpoint = context.IsOutputPort() ? node.GetInputPort(portId) : node.GetOutputPort(portId);
            m_Graph.CreatePortConnection(context.IsOutputPort() ? context : endpoint,
                context.IsOutputPort() ? endpoint : context);
        }

        public FlowNode AddAuthoringNode(Type type, string id, Vector2 position)
        {
            var node = (FlowNode)m_Graph.AddNode(type, position);
            Edit("配置图节点身份", () => node.ConfigureAuthoringIdentity(id));
            return node;
        }

        public BinderConnection ConnectAuthoringPorts(string id, Node source, string output, Node target, string input)
        {
            BinderConnection connection = m_Graph.CreatePortConnection(((FlowNode)source).GetOutputPort(output),
                ((FlowNode)target).GetInputPort(input));
            Edit("配置图连接身份", () => connection.ConfigureAuthoringIdentity(id));
            return connection;
        }

        public void ConfigureNodeMetadata(Node node, string name, bool breakpoint) =>
            Edit("配置图节点", () => { node.name = name; node.isBreakpoint = breakpoint; });

        public void ConfigureValueInput<T>(FlowNode node, string portId, T value) =>
            Edit("配置图常量", () => ((ValueInput)node.GetInputPort(portId)).serializedValue = value);

        // 调用节点按目标子图的最新接口收集端口，并在同一事务内复核端口登记。
        public void ConfigureMacroCall(FlowNode node, BtsmtlMacroGraph macro)
        {
            if (node is not MacroNodeWrapper call || node.graph != m_Graph)
                throw new InvalidOperationException("子图调用必须是当前图中的调用节点。");
            if (!macro)
                throw new ArgumentNullException(nameof(macro));
            Edit("配置子图调用", () =>
            {
                call.macro = macro;
                call.GatherPorts();
                BtsmtlMacroInterface.ValidateCall(call, macro, $"{m_Graph.name}/node:{call.UID}");
            });
        }

        public void ConfigureCanvasGroups(IEnumerable<BtsmtlCanvasGroupDeclaration> groups) =>
            Edit("配置图分组注释", () => BtsmtlCanvasGroupDeclaration.Apply(m_Graph, groups));

        public void ConfigureCanvas(string category, string comments, Vector2 translation, float zoomFactor) =>
            Edit("配置图画布", () =>
            {
                m_Graph.category = category;
                m_Graph.comments = comments;
                m_Graph.translation = translation;
                m_Graph.zoomFactor = zoomFactor;
            });
    }
}
#endif
