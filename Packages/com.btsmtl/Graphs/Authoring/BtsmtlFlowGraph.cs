using System;
using System.Collections.Generic;
using FlowCanvas;
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

#if UNITY_EDITOR
    public sealed partial class BtsmtlGraphRoot : IBtsmtlAuthoringGraph
    {
        [NonSerialized] BtsmtlFlowGraphAuthoring m_Authoring;
        public BtsmtlFlowGraphAuthoring Authoring => m_Authoring ??= new BtsmtlFlowGraphAuthoring(this);
        FlowGraph IBtsmtlAuthoringGraph.Graph => this;
        BtsmtlGraphVariableDeclarations IBtsmtlAuthoringGraph.Variables => Variables;
        bool IBtsmtlAuthoringGraph.PublishesVariables => true;
        public override UnityEngine.Object EditorUndoTarget => Authoring.UndoTarget;

        public void EditAuthoring(string title, Action mutation) => Authoring.Edit(title, mutation);

        public override bool CanAuthorConnection(Port source, Port target, out string reason) =>
            Authoring.CanConnect(source, target, out reason);

        public override Node AddNode(Type nodeType, Vector2 position = default) =>
            Authoring.AddNode(nodeType, () => base.AddNode(nodeType, position));

        public override BinderConnection CreatePortConnection(Port source, Port target) =>
            Authoring.Connect(source, target);

        public override void RemoveNode(Node node, bool recordUndo = true, bool force = false) =>
            EditAuthoring("删除图节点", () => base.RemoveNode(node, false, force));

        public override void RemoveConnection(Connection connection, bool recordUndo = true) =>
            EditAuthoring("删除图连接", () => base.RemoveConnection(connection, false));

        public override void DisconnectPort(Port port) =>
            Authoring.Disconnect(port, connection => base.RemoveConnection(connection, false));

        public override List<Node> DuplicateNodes(List<Node> nodes, Vector2 position = default)
        {
            List<Node> result = null;
            EditAuthoring("复制图节点", () => result = base.DuplicateNodes(nodes, position));
            return result;
        }

        public override void RenameNode(Node node, string name) =>
            EditAuthoring("修改图节点名称", () => node.name = name);

        public override void ClearGraph() => Authoring.Clear(node => base.RemoveNode(node, false, false));

        public override GenericMenu GetNodesMenu(Vector2 position, Port context, UnityEngine.Object instance) =>
            Authoring.NodesMenu(position, context, instance);

        public override void AppendNodeCreationItem(GenericMenu menu, string category, Type type,
            Vector2 position, Port context, object instance) =>
            Authoring.AppendNodeCreationItem(menu, category, type, position, context);

        public override void DrawNodeValueInputsInspectorGUI(FlowNode node) =>
            BtsmtlGraphAuthoringGUI.DrawValueInputs(this, node);
    }
#endif
}
