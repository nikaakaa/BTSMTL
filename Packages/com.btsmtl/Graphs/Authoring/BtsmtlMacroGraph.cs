using System;
using System.Collections.Generic;
using System.Linq;
using FlowCanvas;
using FlowCanvas.Macros;
using NodeCanvas.Framework;
using ParadoxNotion;
using ParadoxNotion.Design;
using UnityEngine;
#if UNITY_EDITOR
using NodeCanvas.Editor;
using UnityEditor;
#endif

namespace BTSMTL.Graphs
{
    // BTSMTL 通用参数化子图。不独立运行：每个调用位置由调用方编译器按出现路径展开。
    public sealed class BtsmtlMacroGraph : Macro, IBtsmtlGraphVariableOwner, IBtsmtlGraphVariableSource,
        IBtsmtlGraphCompilationInputs, IBtsmtlMacroHostProvider
#if UNITY_EDITOR
        , IBtsmtlAuthoringGraph, IBlackboardEditorAdapter
#endif
    {
        [Serializable]
        sealed class AuthoringData
        {
            public object Macro;
            public string Identity;
            public string Revision;
        }

        [SerializeField, HideInInspector] string m_AuthoringId = Guid.NewGuid().ToString("N");
        [SerializeField, HideInInspector] string m_ContentRevision = "1";
        [SerializeReference] BtsmtlMacroHost m_Host;
        [SerializeField] BtsmtlGraphVariableDeclarations m_Variables = new();

        public string AuthoringId => m_AuthoringId;
        public string ContentRevision => m_ContentRevision;
        public BtsmtlMacroHost Host => m_Host;
        public BtsmtlGraphVariableDeclarations Variables
        {
            get
            {
                m_Variables.BindOwner(this);
                return m_Variables;
            }
        }
        public UnityEngine.Object OwnerAsset => this;
        public BtsmtlGraphVariableDeclarations VariableDeclarations => Variables;
        public IBlackboard SourceVariables => Variables.Blackboard;
        public BtsmtlMacroInputNode InputAnchor => allNodes.OfType<BtsmtlMacroInputNode>().SingleOrDefault();
        public BtsmtlMacroOutputNode OutputAnchor => allNodes.OfType<BtsmtlMacroOutputNode>().SingleOrDefault();

        public override bool usesExternalExecution => true;
        public override bool allowsPortIdentityAliases => false;
        public override bool canAcceptVariableDrops => true;
        public override bool requiresAgent => false;
        public override bool requiresPrimeNode => false;
        public override bool isTree => false;
        public override PlanarDirection flowDirection => PlanarDirection.Horizontal;

        public override bool CanAuthorNodeType(Type nodeType) =>
            nodeType == typeof(BtsmtlMacroInputNode) || nodeType == typeof(BtsmtlMacroOutputNode) ||
            m_Host != null && m_Host.CanAuthorNodeType(nodeType);

        public BtsmtlMacroHost CreatePrivateMacroHost() =>
            m_Host?.Clone() ?? throw new InvalidOperationException("子图尚未声明宿主准入。");

        // 参数接口、身份、宿主准入与局部变量语义不在节点快照中，但决定每个调用位置的编译结果。
        public void AppendCompilationInputs(List<string> parts)
        {
            parts.Add(m_AuthoringId);
            parts.Add(m_ContentRevision);
            parts.Add(m_Host?.Identity ?? string.Empty);
            foreach (DynamicParameterDefinition parameter in inputDefinitions.Concat(outputDefinitions))
            {
                parts.Add(parameter.ID);
                parts.Add(parameter.name);
                parts.Add(parameter.type?.AssemblyQualifiedName ?? string.Empty);
            }
            parts.Add(inputDefinitions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            BtsmtlGraphVariableDeclarations variables = Variables;
            parts.Add(variables.OwnerId);
            parts.Add(variables.Revision);
            foreach (BtsmtlBlackboardDeclaration declaration in variables.BlackboardDeclarations)
            {
                parts.Add(declaration.VariableId);
                parts.Add(declaration.Scope.ToString());
                parts.Add(declaration.Lifetime.ToString());
                parts.Add(declaration.Category);
            }
        }

        protected override void OnGraphInitialize() =>
            throw new InvalidOperationException("BTSMTL 子图只能由调用方编译后的编排内容执行。");

        public override object OnDerivedDataSerialization()
        {
            foreach (Node node in allNodes)
            {
                _ = node.UID;
                foreach (Connection connection in node.outConnections)
                    _ = connection.UID;
            }
            return new AuthoringData
            {
                Macro = base.OnDerivedDataSerialization(),
                Identity = m_AuthoringId,
                Revision = m_ContentRevision
            };
        }

        public override void OnDerivedDataDeserialization(object data)
        {
            if (data is not AuthoringData authoring)
                throw new InvalidOperationException("BTSMTL 子图缺少作者数据。");
            base.OnDerivedDataDeserialization(authoring.Macro);
            m_AuthoringId = authoring.Identity;
            m_ContentRevision = authoring.Revision;
        }

#if UNITY_EDITOR
        [NonSerialized] BtsmtlFlowGraphAuthoring m_Authoring;
        public BtsmtlFlowGraphAuthoring Authoring => m_Authoring ??= new BtsmtlFlowGraphAuthoring(this);
        FlowGraph IBtsmtlAuthoringGraph.Graph => this;
        bool IBtsmtlAuthoringGraph.PublishesVariables => false;

        public override bool allowsEditorExecution => false;
        public override bool usesDomainAuthoring => true;
        public override bool usesExplicitPortSelection => true;
        public override bool isEditorReadOnly => Application.isPlaying;
        public override UnityEngine.Object EditorUndoTarget => Authoring.UndoTarget;

        // 新子图：唯一执行入口、出入口锚点与宿主准入一次配置完成；私有子图由所属图写入为子资产。
        public static BtsmtlMacroGraph Create(string name, BtsmtlMacroHost host)
        {
            var macro = CreateInstance<BtsmtlMacroGraph>();
            macro.name = name;
            macro.ConfigureHost(host);
            macro.inputDefinitions.Add(new DynamicParameterDefinition("执行", typeof(Flow)));
            macro.EnsureAnchors();
            return macro;
        }

        public void EditAuthoring(string title, Action mutation) => Authoring.Edit(title, mutation);

        public void ConfigureIdentity(string id, string revision)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(revision))
                throw new ArgumentException("子图必须声明稳定身份和内容修订。");
            EditAuthoring("配置子图身份", () =>
            {
                m_AuthoringId = id;
                m_ContentRevision = revision;
            });
        }

        public void ConfigureHost(BtsmtlMacroHost host) =>
            EditAuthoring("配置子图宿主", () => m_Host = host ?? throw new ArgumentNullException(nameof(host)));

        public void EnsureAnchors() => EditAuthoring("配置子图出入口", () =>
        {
            if (InputAnchor == null)
                base.AddNode(typeof(BtsmtlMacroInputNode), new Vector2(-translation.x + 200, -translation.y + 200));
            if (OutputAnchor == null)
                base.AddNode(typeof(BtsmtlMacroOutputNode), new Vector2(-translation.x + 800, -translation.y + 200));
            InputAnchor.GatherPorts();
            OutputAnchor.GatherPorts();
        });

        public override bool CanAuthorConnection(Port source, Port target, out string reason) =>
            Authoring.CanConnect(source, target, out reason);

        public override Node AddNode(Type nodeType, Vector2 position = default) =>
            Authoring.AddNode(nodeType, () => base.AddNode(nodeType, position));

        public override BinderConnection CreatePortConnection(Port source, Port target) =>
            Authoring.Connect(source, target);

        public override void RemoveNode(Node node, bool recordUndo = true, bool force = false)
        {
            if (node is BtsmtlMacroInputNode || node is BtsmtlMacroOutputNode)
                throw new InvalidOperationException("子图出入口由参数接口持有，不能单独删除。");
            EditAuthoring("删除图节点", () => base.RemoveNode(node, false, force));
        }

        public override void RemoveConnection(Connection connection, bool recordUndo = true) =>
            EditAuthoring("删除图连接", () => base.RemoveConnection(connection, false));

        public override void DisconnectPort(Port port) =>
            Authoring.Disconnect(port, connection => base.RemoveConnection(connection, false));

        public override List<Node> DuplicateNodes(List<Node> nodes, Vector2 position = default)
        {
            List<Node> result = null;
            EditAuthoring("复制图节点", () => result = base.DuplicateNodes(
                nodes.Where(node => node is not BtsmtlMacroInputNode && node is not BtsmtlMacroOutputNode).ToList(),
                position));
            return result;
        }

        public override void RenameNode(Node node, string name) =>
            EditAuthoring("修改图节点名称", () => node.name = name);

        public override void ClearGraph() => Authoring.Clear(node =>
        {
            if (node is not BtsmtlMacroInputNode && node is not BtsmtlMacroOutputNode)
                base.RemoveNode(node, false, false);
        });

        public override GenericMenu GetNodesMenu(Vector2 position, Port context, UnityEngine.Object instance) =>
            Authoring.NodesMenu(position, context, instance);

        public override void AppendNodeCreationItem(GenericMenu menu, string category, Type type,
            Vector2 position, Port context, object instance) =>
            Authoring.AppendNodeCreationItem(menu, category, type, position, context);

        public override void DrawNodeValueInputsInspectorGUI(FlowNode node) =>
            BtsmtlGraphAuthoringGUI.DrawValueInputs(this, node);

        protected override GenericMenu OnNodesContextMenu(GenericMenu menu, Node[] nodes) => menu;

        protected override void OnGraphEditorToolbar()
        {
            GUILayout.Label((AssetDatabase.IsMainAsset(this) ? "共享子图 · " : "私有子图 · ") +
                (m_Host?.DisplayName ?? "未声明宿主"), EditorStyles.miniLabel);
            if (GUILayout.Button("编辑接口", EditorStyles.toolbarButton))
                BtsmtlMacroInterface.OpenEditor(this);
        }

        public FlowNode AddAuthoringNode(Type type, string id, Vector2 position) =>
            Authoring.AddAuthoringNode(type, id, position);

        public BinderConnection ConnectAuthoringPorts(string id, Node source, string output,
            Node target, string input) => Authoring.ConnectAuthoringPorts(id, source, output, target, input);

        public void ConfigureNodeMetadata(Node node, string name, bool breakpoint = false) =>
            Authoring.ConfigureNodeMetadata(node, name, breakpoint);

        public void ConfigureValueInput<T>(FlowNode node, string portId, T value) =>
            Authoring.ConfigureValueInput(node, portId, value);

        public void ConfigureCanvas(string category, string comments, Vector2 translation, float zoomFactor) =>
            Authoring.ConfigureCanvas(category, comments, translation, zoomFactor);

        public void ConfigureCanvasGroups(params BtsmtlCanvasGroupDeclaration[] groups) =>
            Authoring.ConfigureCanvasGroups(groups);

        public override IBlackboard editorBlackboard => Variables.Blackboard;
        public bool IsReadOnly => isEditorReadOnly;
        public bool AllowVariablePick => !IsReadOnly;
        public void DrawBlackboardExtensions(IBlackboard blackboard, UnityEngine.Object contextObject) =>
            EditorGUILayout.LabelField("子图局部变量（每个调用位置独立）", EditorStyles.miniLabel);
        public GenericMenu GetAddVariableMenu(IBlackboard blackboard, UnityEngine.Object contextObject) =>
            BtsmtlGraphAuthoringGUI.VariableCreationMenu(this);
        public GenericMenu GetVariableMenu(IBlackboard blackboard, UnityEngine.Object contextObject,
            Variable variable, int index) => BtsmtlGraphAuthoringGUI.VariableNodeMenu(this, variable, Vector2.zero);
        public void ExecuteMutation(string title, Action mutation) => Variables.Edit(title, mutation);
        public void ApplyVariableList(IBlackboard blackboard, IReadOnlyList<Variable> variables) => Variables.Replace(variables);
        protected override void OnVariableDropInGraph(IBlackboard blackboard, Variable variable,
            Vector2 mousePos) => BtsmtlGraphAuthoringGUI.VariableNodeMenu(this, variable, mousePos).ShowAsContext();

        [MenuItem("Assets/Create/BTSMTL/Graph Macro", false, 82)]
        static void CreateSharedAsset() =>
            ProjectWindowUtil.CreateAsset(Create("Graph Macro", new BtsmtlGraphMacroHost(Array.Empty<string>())),
                "BtsmtlGraphMacro.asset");
#endif
    }

    [DoNotList, ProtectedSingleton, Name("子图输入")]
    public sealed class BtsmtlMacroInputNode : MacroInputNode
    {
#if UNITY_EDITOR
        protected override GenericMenu OnDragAndDropPortContextMenu(GenericMenu menu, Port port) => menu;

        protected override void OnNodeInspectorGUI()
        {
            EditorGUILayout.HelpBox("子图参数由接口编辑器统一维护。", MessageType.Info);
            if (GUILayout.Button("打开接口编辑器"))
                BtsmtlMacroInterface.OpenEditor((BtsmtlMacroGraph)graph);
        }
#endif
    }

    [DoNotList, ProtectedSingleton, Name("子图输出")]
    public sealed class BtsmtlMacroOutputNode : MacroOutputNode
    {
#if UNITY_EDITOR
        protected override GenericMenu OnDragAndDropPortContextMenu(GenericMenu menu, Port port) => menu;

        protected override void OnNodeInspectorGUI()
        {
            EditorGUILayout.HelpBox("子图参数由接口编辑器统一维护。", MessageType.Info);
            if (GUILayout.Button("打开接口编辑器"))
                BtsmtlMacroInterface.OpenEditor((BtsmtlMacroGraph)graph);
        }
#endif
    }
}
