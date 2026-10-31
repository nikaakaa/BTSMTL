using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using FlowCanvas;
using NodeCanvas.Framework;
using UnityEditor;
#endif

namespace BTSMTL.Graphs
{
    // 子图的宿主准入声明：子图内允许哪些节点由将要调用它的宿主图决定，编译时再由实际调用方复核。
    [Serializable]
    public abstract class BtsmtlMacroHost
    {
        public abstract string DisplayName { get; }
        // 准入声明的稳定内容，进入调用方编译身份。
        public abstract string Identity { get; }
        public abstract bool CanAuthorNodeType(Type nodeType);
        // 私有子图在所属图中创建时取所属图的准入；各子图持有独立副本。
        public abstract BtsmtlMacroHost Clone();

#if UNITY_EDITOR
        // 宿主领域可接管子图内节点的作者 Inspector 与值输入绘制，使子图内节点与宿主图中同类节点编辑一致；
        // 返回 false 时使用通用绘制。
        public virtual bool DrawNodeInspector(BtsmtlFlowNode node) => false;
        public virtual bool DrawValueInputs(BtsmtlMacroGraph macro, FlowCanvas.FlowNode node) => false;
        // 宿主领域追加只有本领域能配置完整的节点创建项。
        public virtual void AppendNodesMenu(BtsmtlMacroGraph macro, UnityEditor.GenericMenu menu, Vector2 position,
            FlowCanvas.Port context, UnityEngine.Object instance) { }
        // 新建子图的出入口锚点已创建；宿主领域可按自身作者合同配置锚点身份。
        public virtual void OnMacroCreated(BtsmtlMacroGraph macro) { }
        // 宿主领域可用普通节点定义作作者类型：创建时换成承载定义的画布节点，再按定义完成初始化；
        // 准入与菜单判断读取画布节点所承载的作者类型。
        public virtual Type CanvasNodeType(Type authoringType) => authoringType;
        public virtual void OnNodeCreated(Node node, Type authoringType) { }
        public virtual Type AuthoringNodeType(Node node) => node.GetType();

        public virtual List<Node> DuplicateNodes(BtsmtlMacroGraph macro, List<Node> nodes, Vector2 position)
        {
            List<Node> result = null;
            macro.EditAuthoring("复制图节点", () => result = macro.DuplicateNodeStructure(nodes, position));
            return result;
        }

        public virtual bool HandleEditorCommand(BtsmtlMacroGraph macro, string command, Vector2 position) => false;

        public virtual BtsmtlMacroGraph CreatePrivateMacro(FlowGraph owner, string name)
        {
            string path = AssetDatabase.GetAssetPath(owner);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("所属图保存为资产后才能创建私有子图。");
            BtsmtlMacroGraph macro = BtsmtlMacroGraph.Create(name, this);
            AssetDatabase.AddObjectToAsset(macro, path);
            Undo.RegisterCreatedObjectUndo(macro, "创建私有子图");
            return macro;
        }

        public virtual IEnumerable<BtsmtlMacroGraph> SharedMacros(FlowGraph caller)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(BtsmtlMacroGraph)))
                if (AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is BtsmtlMacroGraph macro)
                    yield return macro;
        }

        public virtual IEnumerable<FlowGraph> CallerGraphs(BtsmtlMacroGraph macro)
        {
            string macroPath = AssetDatabase.GetAssetPath(macro);
            if (string.IsNullOrEmpty(macroPath))
                yield break;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(FlowGraph)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == macroPath || Array.IndexOf(AssetDatabase.GetDependencies(path, false), macroPath) >= 0)
                    foreach (FlowGraph graph in AssetDatabase.LoadAllAssetsAtPath(path).OfType<FlowGraph>())
                        yield return graph;
            }
        }

        public virtual string AuthoringPath(FlowGraph graph) => AssetDatabase.GetAssetPath(graph);
        public virtual string SharedMacroLabel(BtsmtlMacroGraph macro) => macro.name;
        public virtual bool IsShared(BtsmtlMacroGraph macro) => AssetDatabase.IsMainAsset(macro);

        public virtual void Edit(FlowGraph graph, string title, FlowGraph[] affectedGraphs, Action mutation) =>
            BtsmtlFlowGraphAuthoring.EditAssets(graph, title, affectedGraphs, mutation);

        public virtual void DrawAuthoringToolbar(BtsmtlMacroGraph macro) =>
            GUILayout.Label((IsShared(macro) ? "共享子图 · " : "私有子图 · ") + DisplayName, EditorStyles.miniLabel);
#endif
    }

    // 能在自身中创建私有子图的图提供其准入声明。
    public interface IBtsmtlMacroHostProvider
    {
        BtsmtlMacroHost CreatePrivateMacroHost();
    }

    // 通用图宿主：与 BtsmtlGraphRoot 共用节点准入，领域集合必须是调用方图领域的子集。
    [Serializable]
    public sealed class BtsmtlGraphMacroHost : BtsmtlMacroHost
    {
        [SerializeField] string[] m_Domains = Array.Empty<string>();

        BtsmtlGraphMacroHost() { }

        public BtsmtlGraphMacroHost(IEnumerable<string> domains)
        {
            string[] values = domains?.ToArray() ?? throw new ArgumentNullException(nameof(domains));
            if (values.Any(string.IsNullOrWhiteSpace) ||
                values.Distinct(StringComparer.Ordinal).Count() != values.Length)
                throw new ArgumentException("子图宿主领域必须是不重复的稳定身份。", nameof(domains));
            m_Domains = values.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        }

        public IReadOnlyList<string> Domains => m_Domains;
        public override string DisplayName => m_Domains.Length == 0
            ? "通用图" : "通用图（" + string.Join("、", m_Domains) + "）";
        public override string Identity => "graph:" + string.Join(",", m_Domains);
        public override bool CanAuthorNodeType(Type nodeType) => BtsmtlGraphRoot.AllowsNodeType(nodeType, m_Domains);
        public override BtsmtlMacroHost Clone() => new BtsmtlGraphMacroHost(m_Domains);
    }
}
