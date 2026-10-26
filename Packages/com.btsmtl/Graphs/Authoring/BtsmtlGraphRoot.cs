using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Authoring.Graph;
using BTSMTL.Orchestration;
using FlowCanvas;
using FlowCanvas.Macros;
using NodeCanvas.Framework;
using UnityEngine;
using UnityEngine.Scripting;
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
    public sealed partial class BtsmtlGraphRoot : BtsmtlFlowGraph, IBtsmtlMacroHostProvider
#if UNITY_EDITOR
        , IBlackboardEditorAdapter
#endif
    {
        [SerializeField] string m_AuthoringId = Guid.NewGuid().ToString("N");
        [SerializeField] string m_ContentRevision = "1";
        [SerializeField] UnityEngine.Object m_VariableOwner;
        [SerializeField] BtsmtlGraphEntryDeclaration[] m_Entries = Array.Empty<BtsmtlGraphEntryDeclaration>();
        [SerializeField] string[] m_Domains = Array.Empty<string>();

        public override string AuthoringId => m_AuthoringId;
        // 本图使用的领域；只有这些领域的扩展与输出节点可以放入，宿主合同必须提供全部领域。
        public IReadOnlyList<string> Domains => m_Domains;
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

        public void ConfigureDomains(string[] domains)
        {
            if (domains == null || domains.Any(string.IsNullOrWhiteSpace) ||
                domains.Distinct(StringComparer.Ordinal).Count() != domains.Length)
                throw new ArgumentException("图领域必须是不重复的稳定身份。", nameof(domains));
            m_Domains = domains.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        public override bool CanAuthorNodeType(Type nodeType) => AllowsNodeType(nodeType, m_Domains);
        public BtsmtlMacroHost CreatePrivateMacroHost() => new BtsmtlGraphMacroHost(m_Domains);

        // 通用图与声明通用图宿主的子图共用的节点准入；子图调用的目标内容由校验器按调用方复核。
        public static bool AllowsNodeType(Type nodeType, IReadOnlyList<string> domains) =>
            nodeType == typeof(MacroNodeWrapper) ||
            Attribute.IsDefined(nodeType, typeof(BtsmtlGraphOperationAttribute), false) ||
            typeof(IBtsmtlVariableFlowNode).IsAssignableFrom(nodeType) ||
            typeof(IBtsmtlHostInputFlowNode).IsAssignableFrom(nodeType) ||
            BtsmtlNativeValueNodeCatalog.TryGetNode(nodeType, out _) ||
            typeof(BtsmtlFlowNode).IsAssignableFrom(nodeType) &&
            GraphNodeDomains.TryGet(nodeType, out string domain) && domains.Contains(domain, StringComparer.Ordinal);

        [Serializable, Preserve]
        sealed class IdentityData
        {
            [Preserve] public string Identity;
            [Preserve] public string Revision;
            [Preserve] public string VariableOwnerId;
            [Preserve] public string VariableRevision;
            [Preserve] public BtsmtlGraphEntryDeclaration[] Entries;
            [Preserve] public string[] Domains;
        }

#if UNITY_EDITOR
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
                Entries = m_Entries,
                Domains = m_Domains
            };
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
