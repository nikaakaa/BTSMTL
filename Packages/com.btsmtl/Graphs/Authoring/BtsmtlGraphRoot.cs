using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Orchestration;
using FlowCanvas;
using NodeCanvas.Framework;
using UnityEngine;
#if UNITY_EDITOR
using NodeCanvas.Editor;
using UnityEditor;
#endif

namespace BTSMTL.Graphs
{
    [Serializable]
    public sealed class BtsmtlGraphEntryDeclaration
    {
        [SerializeField] string m_EntryId;
        [SerializeField] string m_NodeId;
        [SerializeField] OrchestrationExecutionContract m_Contract;

        public BtsmtlGraphEntryDeclaration(string entryId, string nodeId,
            OrchestrationExecutionContract contract)
        {
            m_EntryId = entryId;
            m_NodeId = nodeId;
            m_Contract = contract;
        }
        public string EntryId => m_EntryId;
        public string NodeId => m_NodeId;
        public OrchestrationExecutionContract Contract => m_Contract;
    }

    [CreateAssetMenu(fileName = "BtsmtlGraph", menuName = "BTSMTL/Graph")]
    public sealed partial class BtsmtlGraphRoot : BtsmtlFlowGraph
#if UNITY_EDITOR
        , IBlackboardEditorAdapter
#endif
    {
        [SerializeField] string m_AuthoringId = Guid.NewGuid().ToString("N");
        [SerializeField] string m_ContentRevision = "1";
        [SerializeField] UnityEngine.Object m_VariableOwner;
        [SerializeField] BtsmtlGraphEntryDeclaration[] m_Entries = Array.Empty<BtsmtlGraphEntryDeclaration>();

        public override string AuthoringId => m_AuthoringId;
        public string ContentRevision => m_ContentRevision;
        public IBtsmtlGraphVariableOwner VariableOwner => (IBtsmtlGraphVariableOwner)m_VariableOwner;
        public BtsmtlGraphVariableDeclarations Variables => VariableOwner.VariableDeclarations;
        public override IBlackboard SourceVariables => Variables.Blackboard;
        public IReadOnlyList<BtsmtlGraphEntryDeclaration> Entries => m_Entries;

        public void ConfigureIdentity(string id, string revision)
        {
            m_AuthoringId = id;
            m_ContentRevision = revision;
        }

        public void ConfigureVariableOwner(UnityEngine.Object owner)
        {
            if (owner && owner is not IBtsmtlGraphVariableOwner)
                throw new ArgumentException("变量来源必须是正式变量声明资产。", nameof(owner));
            m_VariableOwner = owner;
            if (owner)
                VariableOwner.VariableDeclarations.BindOwner(VariableOwner.OwnerAsset);
        }

        public void ConfigureEntries(BtsmtlGraphEntryDeclaration[] entries) => m_Entries = entries;

        public override bool CanAuthorNodeType(Type nodeType) =>
            Attribute.IsDefined(nodeType, typeof(BtsmtlGraphOperationAttribute), false) ||
            typeof(IBtsmtlVariableFlowNode).IsAssignableFrom(nodeType) ||
            typeof(IBtsmtlHostInputFlowNode).IsAssignableFrom(nodeType) ||
            BtsmtlNativeValueNodeCatalog.TryGetNode(nodeType, out _);

#if UNITY_EDITOR
        [Serializable]
        sealed class IdentityData
        {
            public string Identity;
            public string Revision;
            public string VariableOwnerId;
            public string VariableRevision;
            public BtsmtlGraphEntryDeclaration[] Entries;
        }

        public override object OnDerivedDataSerialization()
        {
            foreach (Node node in allNodes)
            {
                _ = node.UID;
                foreach (Connection connection in node.outConnections)
                    _ = connection.UID;
            }
            return new IdentityData
            {
                Identity = AuthoringId,
                Revision = ContentRevision,
                VariableOwnerId = m_VariableOwner ? Variables.OwnerId : string.Empty,
                VariableRevision = m_VariableOwner ? Variables.Revision : string.Empty,
                Entries = m_Entries
            };
        }

        public FlowNode AddAuthoringNode(Type type, string id, Vector2 position)
        {
            var node = (FlowNode)AddNode(type, position);
            EditAuthoring("配置图节点身份", () => node.ConfigureAuthoringIdentity(id));
            return node;
        }

        public BinderConnection ConnectAuthoringPorts(string id, Node source, string output,
            Node target, string input)
        {
            BinderConnection connection = CreatePortConnection(((FlowNode)source).GetOutputPort(output),
                ((FlowNode)target).GetInputPort(input));
            EditAuthoring("配置图连接身份", () => connection.ConfigureAuthoringIdentity(id));
            return connection;
        }

        public void ConfigureNodeMetadata(Node node, string name, bool breakpoint = false) =>
            EditAuthoring("配置图节点", () => { node.name = name; node.isBreakpoint = breakpoint; });

        public void ConfigureValueInput<T>(FlowNode node, string portId, T value) =>
            EditAuthoring("配置图常量", () => ((ValueInput)node.GetInputPort(portId)).serializedValue = value);

        public void ConfigureCanvas(string category, string comments, Vector2 translation, float zoomFactor) =>
            EditAuthoring("配置图画布", () =>
            {
                this.category = category;
                this.comments = comments;
                this.translation = translation;
                this.zoomFactor = zoomFactor;
            });

        public void ClearAuthoringContent() => ClearGraph();
        public override IBlackboard editorBlackboard => m_VariableOwner ? Variables.Blackboard : null;
        public bool IsReadOnly => isEditorReadOnly;
        public bool AllowVariablePick => !IsReadOnly;
        public void DrawBlackboardExtensions(IBlackboard blackboard, UnityEngine.Object contextObject) =>
            EditorGUILayout.LabelField("宿主实例变量", EditorStyles.miniLabel);
        public GenericMenu GetAddVariableMenu(IBlackboard blackboard, UnityEngine.Object contextObject) =>
            BtsmtlGraphAuthoringGUI.VariableCreationMenu(this);
        public GenericMenu GetVariableMenu(IBlackboard blackboard, UnityEngine.Object contextObject,
            Variable variable, int index) => BtsmtlGraphAuthoringGUI.VariableNodeMenu(this, variable, Vector2.zero);
        public void ExecuteMutation(string title, Action mutation) => Variables.Edit(title, mutation);
        public void ApplyVariableList(IBlackboard blackboard, IReadOnlyList<Variable> variables) => Variables.Replace(variables);
        protected override void OnVariableDropInGraph(IBlackboard blackboard, Variable variable,
            Vector2 mousePos) => BtsmtlGraphAuthoringGUI.VariableNodeMenu(this, variable, mousePos).ShowAsContext();
#endif
    }
}
