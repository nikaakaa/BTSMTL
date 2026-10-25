using System;
using System.Collections.Generic;
using System.Linq;
using FlowCanvas;
using FlowCanvas.Nodes;
using NodeCanvas.Framework;
using ParadoxNotion;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BTSMTL.Graphs
{
    public abstract class BtsmtlFlowGraph : FlowGraph, IBtsmtlGraphVariableSource
    {
        public abstract string AuthoringId { get; }
        public virtual IBlackboard SourceVariables => blackboard;
        public override bool canAcceptVariableDrops => true;
        public override bool allowsPortIdentityAliases => false;
        public override bool allowBlackboardOverrides => false;
        public override bool requiresAgent => false;
        public override bool requiresPrimeNode => false;
        public override bool isTree => false;
        public override Type baseNodeType => typeof(FlowScriptNode);
        public override PlanarDirection flowDirection => PlanarDirection.Horizontal;

        protected override void OnGraphInitialize() =>
            throw new InvalidOperationException("BTSMTL 图必须使用已发布的编排内容执行。");

#if UNITY_EDITOR
        public override bool allowsEditorExecution => false;
        public override bool usesDomainAuthoring => true;
        public override bool usesExplicitPortSelection => true;
        public override bool isEditorReadOnly => Application.isPlaying;
#endif
    }

    public sealed partial class BtsmtlGraphRoot
    {
#if UNITY_EDITOR
        [NonSerialized] int m_EditDepth;
        static int s_ConnectionValidationDepth;
        public override UnityEngine.Object EditorUndoTarget => m_EditDepth == 0 ? this : null;

        public void EditAuthoring(string title, Action mutation)
        {
            if (m_EditDepth != 0)
            {
                mutation();
                return;
            }
            if (isEditorReadOnly)
                throw new InvalidOperationException("运行观察期间不能编辑 BTSMTL 图。");
            SelfSerialize();
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(title);
            Undo.RegisterCompleteObjectUndo(this, title);
            m_EditDepth++;
            try
            {
                mutation();
                UpdateNodeIDs(true);
                SelfSerialize();
                EditorUtility.SetDirty(this);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.FlushUndoRecordObjects();
                Undo.RevertAllDownToGroup(group);
                SelfDeserialize();
                Validate();
                throw;
            }
            finally
            {
                m_EditDepth--;
            }
        }

        public override bool CanAuthorConnection(Port source, Port target, out string reason)
        {
            reason = null;
            if (source.parent.graph != this || target.parent.graph != this)
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

        public override Node AddNode(Type nodeType, Vector2 position = default)
        {
            if (!CanAuthorNodeType(nodeType))
                throw new InvalidOperationException($"节点 '{nodeType.FullName}' 不属于当前图的作者合同。");
            Node created = null;
            EditAuthoring("创建图节点", () =>
            {
                created = base.AddNode(nodeType, position);
                _ = created.UID;
            });
            return created;
        }

        public override BinderConnection CreatePortConnection(Port source, Port target)
        {
            if (!CanAuthorConnection(source, target, out string reason))
                throw new InvalidOperationException(reason);
            BinderConnection connection = null;
            EditAuthoring("连接图端口", () => connection =
                BinderConnection.CreateValidatedForDomain(source, target, null));
            return connection;
        }

        public override void RemoveNode(Node node, bool recordUndo = true, bool force = false) =>
            EditAuthoring("删除图节点", () => base.RemoveNode(node, false, force));

        public override void RemoveConnection(Connection connection, bool recordUndo = true) =>
            EditAuthoring("删除图连接", () => base.RemoveConnection(connection, false));

        public override void DisconnectPort(Port port) => EditAuthoring("断开图端口", () =>
        {
            foreach (Connection connection in port.GetPortConnections().ToArray())
                base.RemoveConnection(connection, false);
        });

        public override List<Node> DuplicateNodes(List<Node> nodes, Vector2 position = default)
        {
            List<Node> result = null;
            EditAuthoring("复制图节点", () => result = base.DuplicateNodes(nodes, position));
            return result;
        }

        public override void RenameNode(Node node, string name) =>
            EditAuthoring("修改图节点名称", () => node.name = name);

        public override void ClearGraph() => EditAuthoring("清理图节点", () =>
        {
            foreach (Node node in allNodes.ToArray())
                base.RemoveNode(node, false, false);
            canvasGroups = new List<NodeCanvas.Framework.CanvasGroup>();
        });

        public override GenericMenu GetNodesMenu(Vector2 position, Port context, UnityEngine.Object instance)
        {
            GenericMenu menu = AppendFlowNodesMenu(new GenericMenu(), string.Empty, position, context, instance);
            foreach (BtsmtlNativeValueNodeDefinition definition in BtsmtlNativeValueNodeCatalog.All)
                AppendNodeCreationItem(menu, "数值/" + definition.Kind,
                    typeof(SimplexNodeWrapper<>).MakeGenericType(definition.NodeType), position, context, null);
            return menu;
        }

        public override void AppendNodeCreationItem(GenericMenu menu, string category, Type type,
            Vector2 position, Port context, object instance)
        {
            Type resolved = type.IsGenericTypeDefinition
                ? type.MakeGenericType(context != null && context.IsValuePort() ? context.type : typeof(float))
                : type;
            if (context == null)
            {
                menu.AddItem(new GUIContent(category), false, () => AddNode(resolved, position));
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
                    () => EditAuthoring("创建并连接图节点", () =>
                    {
                        var node = (FlowNode)AddNode(resolved, position);
                        Port endpoint = context.IsOutputPort()
                            ? node.GetInputPort(candidate.ID) : node.GetOutputPort(candidate.ID);
                        CreatePortConnection(context.IsOutputPort() ? context : endpoint,
                            context.IsOutputPort() ? endpoint : context);
                    }));
        }

        public override void DrawNodeValueInputsInspectorGUI(FlowNode node) =>
            BtsmtlGraphAuthoringGUI.DrawValueInputs(this, node);
#endif
    }

}
