using System;
using System.Linq;
using BTSMTL.Orchestration;
using NodeCanvas.Editor;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Graphs.Editor
{
    [CustomEditor(typeof(BtsmtlGraphRoot))]
    public sealed class BtsmtlGraphRootInspector : UnityEditor.Editor
    {
        string m_Error;

        public override void OnInspectorGUI()
        {
            var graph = (BtsmtlGraphRoot)target;
            using (new EditorGUI.DisabledScope(graph.isEditorReadOnly))
            {
                EditorGUI.BeginChangeCheck();
                string identity = EditorGUILayout.TextField("图身份", graph.AuthoringId);
                string revision = EditorGUILayout.TextField("内容修订", graph.ContentRevision);
                string category = EditorGUILayout.TextField("分类", graph.category);
                string comments = EditorGUILayout.TextArea(graph.comments, GUILayout.MinHeight(45));
                if (EditorGUI.EndChangeCheck())
                    graph.EditAuthoring("编辑图配置", () =>
                    {
                        graph.ConfigureIdentity(identity, revision);
                        graph.ConfigureCanvas(category, comments, graph.translation, graph.zoomFactor);
                    });

                EditorGUI.BeginChangeCheck();
                UnityEngine.Object owner = EditorGUILayout.ObjectField("变量声明资产",
                    graph.VariableOwner?.OwnerAsset, typeof(ScriptableObject), false);
                if (EditorGUI.EndChangeCheck())
                {
                    try
                    {
                        graph.EditAuthoring("设置图变量来源", () => graph.ConfigureVariableOwner(owner));
                        m_Error = null;
                    }
                    catch (ArgumentException error) { m_Error = error.Message; }
                }
                DrawEntries(graph);
            }
            if (!string.IsNullOrEmpty(m_Error))
                EditorGUILayout.HelpBox(m_Error, MessageType.Error);
            if (GUILayout.Button("编辑图"))
                GraphEditor.OpenWindow(graph);
            if (graph.VariableOwner == null)
                EditorGUILayout.HelpBox("先选择变量声明资产，再编辑该资产持有的实例变量。", MessageType.Info);
            else
                BlackboardEditor.ShowVariables(graph.editorBlackboard, graph);
        }

        static void DrawEntries(BtsmtlGraphRoot graph)
        {
            var entries = graph.Entries.ToList();
            BtsmtlEntryFlowNode[] nodes = graph.allNodes.OfType<BtsmtlEntryFlowNode>().ToArray();
            string[] names = nodes.Select(node => node.name).ToArray();
            EditorGUILayout.LabelField("执行入口", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            for (int i = 0; i < entries.Count; i++)
            {
                BtsmtlGraphEntryDeclaration entry = entries[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    string id = EditorGUILayout.TextField("入口身份", entry.EntryId);
                    int index = Array.FindIndex(nodes, node => node.UID == entry.NodeId);
                    int selected = EditorGUILayout.Popup("入口节点", index, names);
                    var contract = (OrchestrationExecutionContract)EditorGUILayout.EnumPopup("执行合同", entry.Contract);
                    entries[i] = new BtsmtlGraphEntryDeclaration(id,
                        selected >= 0 ? nodes[selected].UID : entry.NodeId, contract);
                    if (GUILayout.Button("移除入口"))
                    {
                        entries.RemoveAt(i);
                        GUI.changed = true;
                        break;
                    }
                }
            }
            if (GUILayout.Button("添加入口"))
            {
                entries.Add(new BtsmtlGraphEntryDeclaration(string.Empty, string.Empty,
                    OrchestrationExecutionContract.Complete));
                GUI.changed = true;
            }
            if (EditorGUI.EndChangeCheck())
                graph.EditAuthoring("编辑图入口", () => graph.ConfigureEntries(entries.ToArray()));
        }
    }
}
