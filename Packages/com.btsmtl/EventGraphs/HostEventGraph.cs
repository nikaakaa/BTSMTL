using System;
using System.Collections.Generic;
using System.Linq;
using FlowCanvas;
using FlowCanvas.Macros;
using FlowCanvas.Nodes;
using NodeCanvas.Framework;
using ParadoxNotion;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.EventGraphs
{
    [CreateAssetMenu(
        fileName = "HostEventGraph",
        menuName = "3C/BTSMTL/Host Event Graph")]
    public class HostEventGraph : FlowScript
    {
        [SerializeField] string m_AuthoringId = Guid.NewGuid().ToString("N");
        [SerializeField] string m_ContentRevision = "1";

        [SerializeField, HideInInspector] byte[] m_ExecutionData = Array.Empty<byte>();
        [SerializeField, HideInInspector] string m_ExecutionRevision = string.Empty;
        [NonSerialized] Float32GraphExecutionData m_PreparedExecutionData;
        [NonSerialized] byte[] m_PreparedExecutionBytes;

        public string AuthoringId => m_AuthoringId ?? string.Empty;
        public string ContentRevision => m_ContentRevision ?? string.Empty;

        public Float32GraphExecutionData RequireExecutionData()
        {
            if (m_ExecutionData.Length == 0 || m_ExecutionRevision != ContentRevision)
                throw new InvalidOperationException($"事件图 '{AuthoringId}' 的执行数据未生成或已过期，请显式编译该图。");
            if (!ReferenceEquals(m_PreparedExecutionBytes, m_ExecutionData))
            {
                m_PreparedExecutionData = Float32GraphExecutionDataCodec.Read(m_ExecutionData);
                m_PreparedExecutionBytes = m_ExecutionData;
            }
            return m_PreparedExecutionData;
        }

#if UNITY_EDITOR
        public void SetExecutionData(byte[] bytes)
        {
            m_ExecutionData = bytes;
            m_ExecutionRevision = ContentRevision;
        }
#endif

        public override bool allowBlackboardOverrides => false;
        public override bool requiresAgent => false;
        public override bool requiresPrimeNode => false;
        public override bool canAcceptVariableDrops => true;
        public override bool allowsPortIdentityAliases => false;

        public virtual string GetHostInputDisplayName(string inputId) =>
            "Read " + inputId.Replace(".", " ").Replace("-", " ");

        public EventGraphVariableContract BuildVariableContract()
        {
            var descriptors = new List<EventGraphVariableDescriptor>();
            foreach (Variable variable in blackboard.variables.Values)
            {
                if (!variable.hasStableIdentity)
                    throw new InvalidOperationException(
                        $"Event graph variable '{variable.name}' has no stable identity.");
                if (variable.isDataBound || variable.isPropertyBound)
                    throw new InvalidOperationException($"Event graph variable '{variable.name}' must be a local data declaration.");
                descriptors.Add(new EventGraphVariableDescriptor(
                    new BtsmtlGraphVariableReference(AuthoringId, variable.ID),
                    variable.name,
                    variable.varType,
                    EventGraphValue.FromObject(variable.value),
                    variable.isExposedPublic));
            }
            return new EventGraphVariableContract(
                AuthoringId,
                ContentRevision,
                descriptors);
        }

        public void ConfigureIdentity(string authoringId, string contentRevision)
        {
            if (string.IsNullOrWhiteSpace(authoringId) ||
                string.IsNullOrWhiteSpace(contentRevision))
            {
                throw new ArgumentException("Event graph identity and revision are required.");
            }
            m_AuthoringId = authoringId.Trim();
            m_ContentRevision = contentRevision.Trim();
        }

#if UNITY_EDITOR
        public void ConfigureAuthoringIdentity(
            string authoringId,
            string contentRevision) =>
            HostEventGraphEditorMutation.ConfigureIdentity(
                this,
                authoringId,
                contentRevision);

        public void ClearAuthoringContent() =>
            HostEventGraphEditorMutation.ClearAuthoringContent(this);

        public Variable<T> DeclareVariable<T>(
            string variableId,
            string variableName,
            T defaultValue) =>
            HostEventGraphEditorMutation.DeclareVariable(
                this,
                variableId,
                variableName,
                defaultValue);

        public Node AddAuthoringNode(
            Type nodeType,
            string authoringIdentity,
            Vector2 position = default) =>
            HostEventGraphEditorMutation.AddAuthoringNode(
                this,
                nodeType,
                authoringIdentity,
                position);

        public T AddAuthoringNode<T>(
            string authoringIdentity,
            Vector2 position = default)
            where T : FlowNode =>
            (T)AddAuthoringNode(typeof(T), authoringIdentity, position);

        public void ConfigureNodeMetadata(
            Node node,
            string tag,
            string comments,
            bool isBreakpoint) =>
            HostEventGraphEditorMutation.ConfigureNodeMetadata(
                this,
                node,
                tag,
                comments,
                isBreakpoint);

        public void ConfigureCanvas(
            string category,
            string comments,
            Vector2 translation,
            float zoomFactor) =>
            HostEventGraphEditorMutation.ConfigureCanvas(
                this,
                category,
                comments,
                translation,
                zoomFactor);

        public void BindVariableNode(
            ParameterVariableNode node,
            string variableId) =>
            HostEventGraphEditorMutation.BindVariableNode(
                this,
                node,
                variableId);

        public void ConfigureVariable(
            string variableId,
            bool isExposedPublic) =>
            HostEventGraphEditorMutation.ConfigureVariable(
                this,
                variableId,
                isExposedPublic);

        public void EditAuthoringVariables(string title, Action mutation) =>
            HostEventGraphEditorMutation.EditVariables(this, title, mutation);

        public void ReplaceAuthoringVariables(IReadOnlyList<Variable> variables) =>
            HostEventGraphEditorMutation.ReplaceVariables(this, variables);

        public void ConfigureValueInput<T>(
            FlowNode node,
            string portId,
            T value) =>
            HostEventGraphEditorMutation.ConfigureValueInput(
                this,
                node,
                portId,
                value);

        public void ConfigureHostInput(Node node, string inputId) =>
            HostEventGraphEditorMutation.ConfigureHostInput(
                this,
                node,
                inputId);

        public void ConfigureUpdateEvent(UpdateEvent node) =>
            HostEventGraphEditorMutation.ConfigureUpdateEvent(
                this,
                node);

        public void ConfigureInstantSplit(Split node, int portCount) =>
            HostEventGraphEditorMutation.ConfigureInstantSplit(
                this,
                node,
                portCount);

        public void ConfigureSequence(
            Sequence node,
            int portCount,
            int startIndex) =>
            HostEventGraphEditorMutation.ConfigureSequence(
                this,
                node,
                portCount,
                startIndex);

        public void ConfigureAssignment<T>(
            SetVariable<T> node,
            AssignOp operation,
            bool perSecond) =>
            HostEventGraphEditorMutation.ConfigureAssignment(
                this,
                node,
                operation,
                perSecond);

        public void ConfigureMacro(
            MacroNodeWrapper node,
            Macro macro) =>
            HostEventGraphEditorMutation.ConfigureMacro(
                this,
                node,
                macro);

        public BinderConnection ConnectAuthoringPorts(
            string connectionIdentity,
            Node sourceNode,
            string sourcePortId,
            Node targetNode,
            string targetPortId) =>
            HostEventGraphEditorMutation.ConnectAuthoringPorts(
                this,
                connectionIdentity,
                sourceNode,
                sourcePortId,
                targetNode,
                targetPortId);

#endif

        internal void AdvanceContentRevision()
        {
            m_ContentRevision = Guid.NewGuid().ToString("N");
        }

#if UNITY_EDITOR
        internal Node AddNodeNative(Type nodeType, Vector2 position) =>
            base.AddNode(nodeType, position);

        internal BinderConnection CreatePortConnectionNative(
            Port source,
            Port target) =>
            base.CreatePortConnection(source, target);

        internal void RemoveNodeNative(
            Node node,
            bool recordUndo,
            bool force) =>
            base.RemoveNode(node, recordUndo, force);

        internal void RemoveConnectionNative(
            Connection connection,
            bool recordUndo) =>
            base.RemoveConnection(connection, recordUndo);

        internal List<Node> DuplicateNodesNative(
            List<Node> nodes,
            Vector2 originPosition) =>
            base.DuplicateNodes(nodes, originPosition);

        internal void ClearNativeNodes()
        {
            canvasGroups = null;
            foreach (Node node in allNodes.ToArray())
                base.RemoveNode(node, false, false);
        }
#endif

        protected override void OnGraphInitialize() =>
            throw new InvalidOperationException("HostEventGraph 只通过 BTSMTL 已准备的执行数据运行。");

#if UNITY_EDITOR
        public override bool usesDomainAuthoring => true;
        public override bool allowsEditorExecution => false;
        public override bool isEditorReadOnly => Application.isPlaying;

        public override bool CanAuthorNodeType(Type nodeType) =>
            EventGraphCapabilityCatalog.Allows(nodeType);

        public override bool CanAuthorConnection(
            Port source,
            Port target,
            out string reason) =>
            HostEventGraphEditorMutation.CanConnect(
                this,
                source,
                target,
                out reason);

        public override UnityEditor.GenericMenu GetNodesMenu(
            Vector2 position,
            Port context,
            UnityEngine.Object dropInstance)
        {
            return AppendFlowNodesMenu(
                new UnityEditor.GenericMenu(),
                string.Empty,
                position,
                context,
                dropInstance);
        }

        public override void AppendNodeCreationItem(
            UnityEditor.GenericMenu menu,
            string category,
            Type type,
            Vector2 position,
            Port context,
            object dropInstance) =>
            HostEventGraphEditorMutation.AppendCreationItem(
                this,
                menu,
                category,
                type,
                position,
                context,
                dropInstance);

        public override Node AddNode(
            Type nodeType,
            Vector2 position = default) =>
            HostEventGraphEditorMutation.AddNode(
                this,
                nodeType,
                position);

        public override BinderConnection CreatePortConnection(
            Port source,
            Port target) =>
            HostEventGraphEditorMutation.CreatePortConnection(
                this,
                source,
                target);

        public override void DisconnectPort(Port port) =>
            HostEventGraphEditorMutation.DisconnectPort(this, port);

        public override void RemoveNode(
            Node node,
            bool recordUndo = true,
            bool force = false) =>
            HostEventGraphEditorMutation.RemoveNode(
                this,
                node,
                recordUndo,
                force);

        public override void RemoveConnection(
            Connection connection,
            bool recordUndo = true) =>
            HostEventGraphEditorMutation.RemoveConnection(
                this,
                connection,
                recordUndo);

        public override List<Node> DuplicateNodes(
            List<Node> nodes,
            Vector2 originPosition = default) =>
            HostEventGraphEditorMutation.DuplicateNodes(
                this,
                nodes,
                originPosition);

        public override void ClearGraph() =>
            HostEventGraphEditorMutation.Clear(this);

        public override bool HandleEditorCommand(
            string command,
            Vector2 position) =>
            HostEventGraphEditorMutation.HandleCommand(
                this,
                command,
                position);

        protected override void OnVariableDropInGraph(
            IBlackboard blackboard,
            Variable variable,
            Vector2 mousePosition) =>
            HostEventGraphEditorMutation.HandleBlackboardVariableDrop(
                this,
                blackboard,
                variable,
                mousePosition);

        public override UnityEngine.Object EditorUndoTarget =>
            HostEventGraphEditorMutation.UndoTarget(this);
#endif
    }
}
