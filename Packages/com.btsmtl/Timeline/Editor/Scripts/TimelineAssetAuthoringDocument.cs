using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Graphs;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    public sealed class TimelineAssetAuthoringDocument : ITimelineAuthoringDocument, ITimelinePreviewInputSchemeStorage
    {
        public TimelineAssetAuthoringDocument(UnityEngine.Object source)
        {
            SourcePath = AssetDatabase.GetAssetPath(source);
        }

        public string SourcePath { get; }
        public bool HasUnsavedChanges => AssetDatabase.LoadAllAssetsAtPath(SourcePath).Any(EditorUtility.IsDirty);

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
                    foreach (Track track in scheme.InputTimeline.Data.Tracks)
                    {
                        foreach (Clip clip in track.Clips)
                            if (clip is TreeClip tree)
                                referenced.Add(tree.AssetTree);
                        foreach (TimelineMarker marker in track.Markers)
                            referenced.Add(marker.Graph);
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
}
