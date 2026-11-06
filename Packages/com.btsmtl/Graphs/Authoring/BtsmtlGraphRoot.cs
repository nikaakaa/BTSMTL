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
    public sealed partial class BtsmtlGraphRoot : BtsmtlFlowGraph, IBtsmtlMacroHostProvider
#if UNITY_EDITOR
        , IBlackboardEditorAdapter, IBtsmtlGraphDocumentCompilationSource
#endif
    {
        [NonSerialized] BtsmtlGraphDocument m_Source;
        [NonSerialized] IBtsmtlGraphVariableOwner m_VariableOwner;

        public BtsmtlGraphDocument Source => m_Source;
        public override string AuthoringId => Source.AuthoringId;
        public IReadOnlyList<string> Domains => Source.Domains;
        public string ContentRevision => Source.ContentRevision;
        public IBtsmtlGraphVariableOwner VariableOwner => m_VariableOwner;
        public BtsmtlGraphVariableDeclarations Variables => VariableOwner.VariableDeclarations;
        public BtsmtlGraphVariableEditor VariableEditor => VariableOwner.VariableEditor;
        public override IBlackboard SourceVariables => VariableEditor.Blackboard;
        public IReadOnlyList<BtsmtlGraphEntryDeclaration> Entries => Source.Entries;

        public void BindSource(BtsmtlGraphDocument source, IBtsmtlGraphVariableOwner variableOwner)
        {
            m_Source = source;
            m_VariableOwner = variableOwner;
            hideFlags = HideFlags.HideAndDontSave;
        }

        public void ConfigureIdentity(string id, string revision)
        {
            Source.ConfigureIdentity(id, revision);
        }

        public void ConfigureVariableOwner(UnityEngine.Object owner)
        {
            if (owner && owner is not IBtsmtlGraphVariableOwner)
                throw new ArgumentException("变量来源必须是正式变量声明资产。", nameof(owner));
            m_VariableOwner = (IBtsmtlGraphVariableOwner)owner;
            Source.ConfigureVariableOwner(m_VariableOwner.VariableDeclarations.OwnerId);
        }

        public void ConfigureEntries(BtsmtlGraphEntryDeclaration[] entries) => Source.ConfigureEntries(entries);

        public void ConfigureDomains(string[] domains)
        {
            if (domains == null || domains.Any(string.IsNullOrWhiteSpace) ||
                domains.Distinct(StringComparer.Ordinal).Count() != domains.Length)
                throw new ArgumentException("图领域必须是不重复的稳定身份。", nameof(domains));
            Source.ConfigureDomains(domains.OrderBy(value => value, StringComparer.Ordinal));
        }

        public override bool CanAuthorNodeType(Type nodeType) => AllowsNodeType(nodeType, Domains);
        public BtsmtlMacroHost CreatePrivateMacroHost() => new BtsmtlGraphMacroHost(Domains);

        // 通用图与声明通用图宿主的子图共用的节点准入；子图调用的目标内容由校验器按调用方复核。
        public static bool AllowsNodeType(Type nodeType, IReadOnlyList<string> domains) =>
            nodeType == typeof(MacroNodeWrapper) ||
            Attribute.IsDefined(nodeType, typeof(BtsmtlGraphOperationAttribute), false) ||
            typeof(IBtsmtlVariableFlowNode).IsAssignableFrom(nodeType) ||
            typeof(IBtsmtlHostInputFlowNode).IsAssignableFrom(nodeType) ||
            BtsmtlNativeValueNodes.TryGetNode(nodeType, out _) ||
            typeof(BtsmtlFlowNode).IsAssignableFrom(nodeType) &&
            GraphNodeDomains.TryGet(nodeType, out string domain) && domains.Contains(domain, StringComparer.Ordinal);

#if UNITY_EDITOR
        bool IBtsmtlGraphDocumentCompilationSource.UsesDocumentSource => true;
        string IBtsmtlGraphDocumentCompilationSource.DocumentCompilationInput => BtsmtlGraphDocumentJson.CompilationInput(Source, Variables);
        IReadOnlyList<UnityEngine.Object> IBtsmtlGraphDocumentCompilationSource.DocumentCompilationReferences => Authoring.Document.References;

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

        public void ConfigureMacroCall(FlowNode node, BtsmtlMacroGraph macro) =>
            Authoring.ConfigureMacroCall(node, macro);

        // 生成的整根重建即明确清空页面：节点、连接与分组一起清除，再按声明重建。
        public void ClearAuthoringContent() => ClearGraph();
        public override IBlackboard editorBlackboard => m_VariableOwner != null ? VariableEditor.Blackboard : null;
        public bool IsReadOnly => isEditorReadOnly;
        public bool AllowVariablePick => !IsReadOnly;
        public void DrawBlackboardExtensions(IBlackboard blackboard, UnityEngine.Object contextObject) =>
            EditorGUILayout.LabelField("宿主实例变量", EditorStyles.miniLabel);
        public GenericMenu GetAddVariableMenu(IBlackboard blackboard, UnityEngine.Object contextObject) =>
            BtsmtlGraphAuthoringGUI.VariableCreationMenu(this);
        public GenericMenu GetVariableMenu(IBlackboard blackboard, UnityEngine.Object contextObject,
            Variable variable, int index) => BtsmtlGraphAuthoringGUI.VariableNodeMenu(this, variable, Vector2.zero);
        public void ExecuteMutation(string title, Action mutation) => VariableEditor.Edit(title, mutation);
        public void ApplyVariableList(IBlackboard blackboard, IReadOnlyList<Variable> variables) => VariableEditor.Replace(variables);
        protected override void OnVariableDropInGraph(IBlackboard blackboard, Variable variable,
            Vector2 mousePos) => BtsmtlGraphAuthoringGUI.VariableNodeMenu(this, variable, mousePos).ShowAsContext();
#endif
    }
}
