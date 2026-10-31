using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Graphs;
using NodeCanvas.Framework;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    public sealed class TimelineAssetAuthoringDocument : ITimelineAuthoringDocument, ITimelinePreviewInputSchemeStorage
    {
        sealed class AssetGraphs
        {
            internal Dictionary<string, ScriptableObject> ById;
        }

        static readonly Dictionary<string, AssetGraphs> s_LocalGraphs = new(StringComparer.Ordinal);
        static readonly Dictionary<string, List<ScriptableObject>> s_SharedGraphs = new(StringComparer.Ordinal);
        static readonly Dictionary<string, (string Id, ScriptableObject Graph)> s_SharedSources = new(StringComparer.Ordinal);

        readonly UnityEngine.Object m_Source;
        AssetGraphs m_Graphs;

        static TimelineAssetAuthoringDocument()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (Type type in TypeCache.GetTypesDerivedFrom<ITimelineTreeGraphAsset>())
                if (!type.IsAbstract && typeof(ScriptableObject).IsAssignableFrom(type))
                    foreach (string guid in AssetDatabase.FindAssets("t:" + type.Name))
                        paths.Add(AssetDatabase.GUIDToAssetPath(guid));
            foreach (string path in paths)
                RefreshSharedGraph(path);
            Graph.onGraphSerialized += OnGraphChanged;
            ObjectChangeEvents.changesPublished += OnObjectsChanged;
            TimelineAssetGraphChanges.Changed += OnAssetsChanged;
        }

        public TimelineAssetAuthoringDocument(UnityEngine.Object source)
        {
            m_Source = source;
        }

        public string SourcePath => AssetDatabase.GetAssetPath(m_Source);
        public bool HasUnsavedChanges => AssetDatabase.LoadAllAssetsAtPath(SourcePath).Any(EditorUtility.IsDirty);

        public void Prepare(TimelineData timeline, ScriptableObject graph = null)
        {
            string path = SourcePath;
            if (graph == null)
                ReadLocalGraphs(path);
            else
            {
                if (!s_LocalGraphs.ContainsKey(path))
                    ReadLocalGraphs(path);
                if (string.Equals(AssetDatabase.GetAssetPath(graph), path, StringComparison.Ordinal))
                    UpdateLocalGraph(path, graph);
                if (AssetDatabase.IsMainAsset(graph))
                    SetSharedGraph(AssetDatabase.GetAssetPath(graph), graph);
            }
            m_Graphs = s_LocalGraphs[path];
            timeline.BindAuthoringDocument(this);
        }

        public ScriptableObject ResolveGraph(string authoringId)
        {
            m_Graphs.ById.TryGetValue(authoringId, out ScriptableObject result);
            if (s_SharedGraphs.TryGetValue(authoringId, out List<ScriptableObject> shared))
                foreach (ScriptableObject graph in shared)
                {
                    if (result != null && !ReferenceEquals(result, graph))
                        throw new InvalidOperationException(
                            $"Timeline '{SourcePath}' 的节点图 ID '{authoringId}' 同时指向 '{AssetDatabase.GetAssetPath(result)}/{result.name}' 与 '{AssetDatabase.GetAssetPath(graph)}/{graph.name}'。");
                    result = graph;
                }
            return result != null ? result : throw new InvalidOperationException(
                $"Timeline '{SourcePath}' 找不到节点图 ID '{authoringId}'；引用必须属于同一资产或独立共享图资产。");
        }

        static UnityEngine.Object[] ReadLocalGraphs(string path, HashSet<int> destroyed = null)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            var graphs = new Dictionary<string, ScriptableObject>(StringComparer.Ordinal);
            foreach (UnityEngine.Object asset in assets)
                if (asset is ScriptableObject scriptable && asset is ITimelineTreeGraphAsset graph &&
                    (graph.IsTimelineTree || graph.IsTimelineTrigger) &&
                    (destroyed == null || !destroyed.Contains(asset.GetInstanceID())))
                    AddLocalGraph(path, graphs, scriptable, graph.AuthoringId);
            if (s_LocalGraphs.TryGetValue(path, out AssetGraphs scope))
                scope.ById = graphs;
            else
                s_LocalGraphs.Add(path, new AssetGraphs { ById = graphs });
            return assets;
        }

        static void AddLocalGraph(string path, Dictionary<string, ScriptableObject> graphs, ScriptableObject graph, string id)
        {
            if (!graphs.TryAdd(id, graph))
                throw new InvalidOperationException($"资产 '{path}' 有重复的节点图 ID '{id}'。");
        }

        static void UpdateLocalGraph(string path, ScriptableObject source)
        {
            if (!s_LocalGraphs.TryGetValue(path, out AssetGraphs scope))
                return;
            Dictionary<string, ScriptableObject> graphs = scope.ById;
            foreach (string id in graphs.Where(pair => ReferenceEquals(pair.Value, source)).Select(pair => pair.Key).ToArray())
                graphs.Remove(id);
            if (source is ITimelineTreeGraphAsset graph && (graph.IsTimelineTree || graph.IsTimelineTrigger))
                AddLocalGraph(path, graphs, source, graph.AuthoringId);
        }

        static void RemoveSharedGraph(string path)
        {
            if (!s_SharedSources.Remove(path, out var source))
                return;
            List<ScriptableObject> graphs = s_SharedGraphs[source.Id];
            graphs.Remove(source.Graph);
            if (graphs.Count == 0)
                s_SharedGraphs.Remove(source.Id);
        }

        static void SetSharedGraph(string path, ScriptableObject asset)
        {
            RemoveSharedGraph(path);
            if (asset is not ITimelineTreeGraphAsset graph || (!graph.IsTimelineTree && !graph.IsTimelineTrigger))
                return;
            if (!s_SharedGraphs.TryGetValue(graph.AuthoringId, out List<ScriptableObject> graphs))
                s_SharedGraphs.Add(graph.AuthoringId, graphs = new List<ScriptableObject>());
            graphs.Add(asset);
            s_SharedSources.Add(path, (graph.AuthoringId, asset));
        }

        static void RefreshSharedGraph(string path, HashSet<int> destroyed = null)
        {
            Type type = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (type == null || !typeof(ITimelineTreeGraphAsset).IsAssignableFrom(type))
            {
                RemoveSharedGraph(path);
                return;
            }
            var graph = (ScriptableObject)AssetDatabase.LoadMainAssetAtPath(path);
            if (destroyed != null && destroyed.Contains(graph.GetInstanceID()))
                RemoveSharedGraph(path);
            else
                SetSharedGraph(path, graph);
        }

        static void RefreshAsset(string path, HashSet<int> destroyed = null)
        {
            RefreshSharedGraph(path, destroyed);
            if (!s_LocalGraphs.ContainsKey(path))
                return;
            foreach (UnityEngine.Object asset in ReadLocalGraphs(path, destroyed))
                if (asset is TimelineAsset timeline && (destroyed == null || !destroyed.Contains(asset.GetInstanceID())))
                    timeline.Data.BindAuthoringDocument(new TimelineAssetAuthoringDocument(timeline) { m_Graphs = s_LocalGraphs[path] });
        }

        static void OnGraphChanged(ScriptableObject source)
        {
            string path = AssetDatabase.GetAssetPath(source);
            if (path.Length == 0)
                return;
            UpdateLocalGraph(path, source);
            if (AssetDatabase.IsMainAsset(source))
                SetSharedGraph(path, source);
        }

        static void OnObjectsChanged(ref ObjectChangeEventStream stream)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            var destroyed = new HashSet<int>();
            var timelines = new HashSet<TimelineAsset>();
            for (int index = 0; index < stream.length; index++)
                switch (stream.GetEventType(index))
                {
                    case ObjectChangeKind.CreateAssetObject:
                        stream.GetCreateAssetObjectEvent(index, out var created);
                        paths.Add(AssetDatabase.GUIDToAssetPath(created.guid.ToString()));
                        break;
                    case ObjectChangeKind.DestroyAssetObject:
                        stream.GetDestroyAssetObjectEvent(index, out var removed);
                        paths.Add(AssetDatabase.GUIDToAssetPath(removed.guid.ToString()));
                        destroyed.Add(removed.instanceId);
                        break;
                    case ObjectChangeKind.ChangeAssetObjectProperties:
                        stream.GetChangeAssetObjectPropertiesEvent(index, out var changed);
                        UnityEngine.Object source = EditorUtility.InstanceIDToObject(changed.instanceId);
                        if (source is ScriptableObject graph && source is ITimelineTreeGraphAsset)
                            OnGraphChanged(graph);
                        else if (source is TimelineAsset timeline)
                            timelines.Add(timeline);
                        break;
                }
            paths.Remove(string.Empty);
            foreach (string path in paths)
                RefreshAsset(path, destroyed);
            foreach (TimelineAsset timeline in timelines)
            {
                string path = AssetDatabase.GetAssetPath(timeline);
                if (!paths.Contains(path) && s_LocalGraphs.ContainsKey(path) && !destroyed.Contains(timeline.GetInstanceID()))
                    timeline.Data.BindAuthoringDocument(new TimelineAssetAuthoringDocument(timeline) { m_Graphs = s_LocalGraphs[path] });
            }
        }

        static void OnAssetsChanged(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in deleted)
            {
                s_LocalGraphs.Remove(path);
                RemoveSharedGraph(path);
            }
            for (int index = 0; index < moved.Length; index++)
            {
                if (s_LocalGraphs.Remove(movedFrom[index], out AssetGraphs graphs))
                    s_LocalGraphs[moved[index]] = graphs;
                RemoveSharedGraph(movedFrom[index]);
            }
            foreach (string path in imported.Concat(moved).Distinct(StringComparer.Ordinal))
                RefreshAsset(path);
        }

        public void Save()
        {
            if (!HasUnsavedChanges)
                return;
            TimelineAuthoringSave.NotifySaving(SourcePath);
            AssetDatabase.SaveAssetIfDirty(AssetDatabase.GUIDFromAssetPath(SourcePath));
        }

        public TimelinePreviewInputSchemes Find(TimelinePreviewRootIdentity root)
        {
            TimelinePreviewInputSchemes found = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(TimelinePreviewInputSchemes)))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is not TimelinePreviewInputSchemes schemes ||
                        !string.Equals(root.Owner, schemes.Owner, StringComparison.Ordinal) ||
                        !string.Equals(root.Timeline, schemes.Timeline, StringComparison.Ordinal))
                        continue;
                    if (found != null)
                        throw new InvalidOperationException(
                            $"预览根有两份输入方案：{AssetDatabase.GetAssetPath(found)} 与 {path}；请删除其中一份。");
                    found = schemes;
                }
            }
            return found;
        }

        public void AddSchemes(TimelineAsset entry, string label, TimelinePreviewInputSchemes schemes)
        {
            string path = AssetDatabase.GetAssetPath(entry);
            if (path.Length == 0)
                throw new InvalidOperationException($"Timeline '{entry.name}' 不在资产中，无法保存输入方案。");
            string directory = System.IO.Path.GetDirectoryName(path)!.Replace('\\', '/');
            AssetDatabase.CreateAsset(schemes,
                AssetDatabase.GenerateUniqueAssetPath($"{directory}/{label}.PreviewInputs.asset"));
        }

        public void AddInputTimeline(TimelinePreviewInputSchemes schemes, TimelineAsset timeline) =>
            AssetDatabase.AddObjectToAsset(timeline, schemes);

        public void RemoveInputTimeline(TimelineAsset timeline) => Undo.DestroyObjectImmediate(timeline);

        public void ReleaseUnreferencedGraphs(TimelinePreviewInputSchemes schemes)
        {
            var referenced = new HashSet<UnityEngine.Object>();
            foreach (TimelineAssetPreviewPreferences scheme in schemes.Schemes)
                if (scheme.InputTimeline)
                {
                    TimelineAuthoringGraphBindings.Prepare(scheme.InputTimeline.Data);
                    foreach (Track track in scheme.InputTimeline.Data.Tracks)
                    {
                        foreach (Clip clip in track.Clips)
                            if (clip is TreeClip tree)
                                referenced.Add(tree.AssetTree);
                        foreach (TimelineMarker marker in track.Markers)
                            referenced.Add(marker.Graph);
                    }
                }
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(schemes)))
                if (asset is ITimelineTreeGraphAsset && !referenced.Contains(asset))
                    Undo.DestroyObjectImmediate(asset);
        }

        public string InputPath(TimelinePreviewInputSchemes schemes) => AssetDatabase.GetAssetPath(schemes);

        public bool OwnsInputTimeline(TimelineData timeline) =>
            timeline?.SerializedOwner is TimelineAsset asset &&
            AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(asset)) is TimelinePreviewInputSchemes;
    }

    sealed class TimelineAssetGraphChanges : AssetPostprocessor
    {
        internal static event Action<string[], string[], string[], string[]> Changed;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) =>
            Changed?.Invoke(imported, deleted, moved, movedFrom);
    }
}
