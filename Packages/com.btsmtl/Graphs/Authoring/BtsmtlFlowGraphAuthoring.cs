#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using FlowCanvas;
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
        // 子图变量只属于调用位置的局部状态，不发布到宿主，也不改变黑板作用域。
        bool PublishesVariables { get; }
    }

    // FlowCanvas 编辑入口的共同实现；通用图和子图因基类不同各持有一份，只转发不复制。
    public sealed class BtsmtlFlowGraphAuthoring
    {
        static int s_ConnectionValidationDepth;
        readonly FlowGraph m_Graph;
        int m_EditDepth;

        public BtsmtlFlowGraphAuthoring(FlowGraph graph) =>
            m_Graph = graph ? graph : throw new ArgumentNullException(nameof(graph));

        public UnityEngine.Object UndoTarget => m_EditDepth == 0 ? m_Graph : null;

        public void Edit(string title, Action mutation)
        {
            if (m_EditDepth != 0)
            {
                mutation();
                return;
            }
            if (m_Graph.isEditorReadOnly)
                throw new InvalidOperationException("运行观察期间不能编辑 BTSMTL 图。");
            m_Graph.SelfSerialize();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(title);
            Undo.RegisterCompleteObjectUndo(m_Graph, title);
            m_EditDepth++;
            try
            {
                mutation();
                m_Graph.UpdateNodeIDs(true);
                m_Graph.SelfSerialize();
                EditorUtility.SetDirty(m_Graph);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(group);
                m_Graph.SelfDeserialize();
                m_Graph.Validate();
                throw;
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
                AppendNodeCreationItem(menu, "数值/" + definition.Kind,
                    typeof(SimplexNodeWrapper<>).MakeGenericType(definition.NodeType), position, context);
            return menu;
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
