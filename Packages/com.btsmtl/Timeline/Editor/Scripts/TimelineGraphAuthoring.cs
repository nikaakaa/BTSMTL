using System;
using System.Linq;
using NodeCanvas.Editor;
using ThirdPersonCharacter.Control.Authoring;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    public static class TimelineGraphAuthoring
    {
        public static BtsmtlSkillFlowGraph CreateSubAsset(UnityEngine.Object container, string graphName, BtsmtlSkillFlowGraphRole role)
        {
            return EnsureSubAsset(container, Guid.NewGuid().ToString("N"), graphName, role);
        }

        public static BtsmtlSkillFlowGraph EnsureSubAsset(UnityEngine.Object container, string identity,
            string graphName, BtsmtlSkillFlowGraphRole role)
        {
            if (role != BtsmtlSkillFlowGraphRole.TimelineBody && role != BtsmtlSkillFlowGraphRole.TimelineTrigger)
                throw new ArgumentOutOfRangeException(nameof(role));
            if (container == null)
                throw new InvalidOperationException("Timeline 缺少序列化资产容器。");
            string path = AssetDatabase.GetAssetPath(container);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Timeline 容器不是持久资产，无法创建图子资产。");
            BtsmtlSkillFlowGraph existing = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<BtsmtlSkillFlowGraph>()
                .SingleOrDefault(value => string.Equals(value.AuthoringId, identity, StringComparison.Ordinal));
            if (existing != null)
            {
                if (existing.Role != role)
                    throw new InvalidOperationException($"Timeline图身份 '{identity}' 的角色不匹配。");
                BtsmtlSkillGraphAssetFactory.EnsureRequiredAnchors(existing);
                return existing;
            }
            var graph = ScriptableObject.CreateInstance<BtsmtlSkillFlowGraph>();
            graph.name = string.IsNullOrWhiteSpace(graphName) ? "Timeline Tree" : graphName.Trim();
            graph.ConfigureIdentity(identity, role);
            AssetDatabase.AddObjectToAsset(graph, path);
            Undo.RegisterCreatedObjectUndo(graph, "创建Timeline节点图");
            BtsmtlSkillGraphAssetFactory.PopulateAnchors(graph);
            if (BtsmtlSkillFlowEditorMutation.CurrentGeneration == null)
                graph.SelfSerialize();
            EditorUtility.SetDirty(graph);
            EditorUtility.SetDirty(container);
            if (BtsmtlSkillFlowEditorMutation.CurrentGeneration == null)
                AssetDatabase.SaveAssetIfDirty(container);
            return graph;
        }

        public static void Open(ScriptableObject source)
        {
            if (source is not BtsmtlSkillFlowGraph graph || (!graph.IsTimelineTree && !graph.IsTimelineTrigger))
                throw new InvalidOperationException("Timeline节点图必须是正式TimelineBody或TimelineTrigger。");
            GraphEditor editor = GraphEditor.OpenWindow(graph);
            editor.Show();
            editor.Focus();
        }

        public static void MutateOwnedContent(TimelineData timeline, Action mutation)
        {
            if (BtsmtlSkillFlowEditorMutation.CurrentGeneration != null)
            {
                mutation();
                return;
            }
            var previous = BtsmtlSkillOwnedAssets.Collect(timeline.SerializedOwner);
            mutation();
            BtsmtlSkillOwnedAssets.ReleaseUnreferenced(timeline.SerializedOwner, previous);
        }
    }
}
