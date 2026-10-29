using System;
using System.Collections.Generic;
using BTSMTL.Authoring.Graph;
using BTSMTL.Graphs;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    // 一个预览根的具名输入方案：编排根按根程序的正式身份归属（Owner），独立 Timeline 按自身作者身份归属（Timeline）；
    // 可提交，不参与运行时。资产文件放在哪里、叫什么都不影响归属。
    public sealed partial class TimelinePreviewInputSchemes : ScriptableObject
    {
        public TimelineAssetPreviewPreferences Current => Schemes[Mathf.Clamp(Selected, 0, Schemes.Count - 1)];

        public void ReleaseUnreferencedGraphs()
        {
            var referenced = new HashSet<UnityEngine.Object>();
            foreach (TimelineAssetPreviewPreferences scheme in Schemes)
                if (scheme.InputTimeline)
                    foreach (Track track in scheme.InputTimeline.Data.Tracks)
                    {
                        foreach (Clip clip in track.Clips)
                            if (clip is TreeClip tree)
                                referenced.Add(tree.AssetTree);
                        foreach (TimelineMarker marker in track.Markers)
                            referenced.Add(marker.Graph);
                    }
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(this)))
                if (asset is ITimelineTreeGraphAsset && !referenced.Contains(asset))
                    Undo.DestroyObjectImmediate(asset);
        }
    }

    // 当前预览根输入方案的唯一存储与操作：第一次改动时在根入口 Timeline 旁创建方案资产，此后改动写入该资产；
    // 所有操作只作用于本根。
    public sealed class TimelinePreviewInputSchemeStore
    {
        const string SearchFilter = "t:" + nameof(TimelinePreviewInputSchemes);
        TimelinePreviewRootIdentity m_Root;
        TimelineAsset m_Source;
        string m_Label;
        TimelinePreviewInputSchemes m_Asset;
        TimelineAssetPreviewPreferences m_Default;
        string m_InputsFingerprint;
        TimelineAssetPreviewPreferences m_ReadScheme;

        public TimelineAssetPreviewPreferences Current =>
            m_Asset != null && m_Asset.Schemes.Count != 0 ? m_Asset.Current : m_Default;

        public IReadOnlyList<TimelineAssetPreviewPreferences> All =>
            m_Asset != null && m_Asset.Schemes.Count != 0 ? m_Asset.Schemes : new[] { m_Default };

        public string Path => m_Asset != null ? AssetDatabase.GetAssetPath(m_Asset) : string.Empty;
        public TimelinePreviewRootIdentity Root => m_Root;

        public void Load(TimelinePreviewRoot root)
        {
            Save();
            m_Source = root.Entry;
            m_Label = root.Label;
            m_Root = root.Identity;
            m_Asset = Find(root.Identity);
            m_Default = new TimelineAssetPreviewPreferences();
            m_InputsFingerprint = null;
            m_ReadScheme = null;
        }

        // 当前方案已被修改：尚无方案资产时创建，并标记待保存。
        public void MarkChanged()
        {
            if (m_Asset == null)
                m_Asset = Create();
            if (m_Asset.Schemes.Count == 0)
            {
                m_Asset.Schemes.Add(m_Default);
                m_Asset.Selected = 0;
            }
            EditorUtility.SetDirty(m_Asset);
        }

        public void Save()
        {
            if (m_Asset != null)
                TimelineAuthoringSave.Save(m_Asset);
        }

        public bool Select(int index)
        {
            if (m_Asset == null || index < 0 || index >= m_Asset.Schemes.Count)
                return false;
            m_Asset.Selected = index;
            EditorUtility.SetDirty(m_Asset);
            return true;
        }

        // 复制方案连同输入 Timeline；Timeline 引用的预览输入图共用，不复制。
        public void SaveAs(string name)
        {
            TimelineAssetPreviewPreferences copy =
                JsonUtility.FromJson<TimelineAssetPreviewPreferences>(JsonUtility.ToJson(Current));
            copy.Name = name;
            MarkChanged();
            if (Current.InputTimeline)
            {
                copy.InputTimeline = UnityEngine.Object.Instantiate(Current.InputTimeline);
                copy.InputTimeline.Data.RegenerateAuthoringIdentities();
                copy.InputTimeline.name = name + " 输入";
                AssetDatabase.AddObjectToAsset(copy.InputTimeline, m_Asset);
            }
            m_Asset.Schemes.Add(copy);
            m_Asset.Selected = m_Asset.Schemes.Count - 1;
            m_InputsFingerprint = null;
            Save();
        }

        // 当前方案的输入 Timeline；尚无时在方案资产内创建。
        public TimelineAsset EnsureInputTimeline()
        {
            MarkChanged();
            TimelineAssetPreviewPreferences current = Current;
            if (current.InputTimeline)
            {
                if (current.InputTimeline.Data.EnsureAuthoringIdentities())
                    EditorUtility.SetDirty(current.InputTimeline);
                return current.InputTimeline;
            }
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.Data.EnsureAuthoringIdentities();
            timeline.name = current.Name + " 输入";
            AssetDatabase.AddObjectToAsset(timeline, m_Asset);
            current.InputTimeline = timeline;
            EditorUtility.SetDirty(m_Asset);
            Save();
            m_InputsFingerprint = null;
            return timeline;
        }

        // 输入 Timeline 是输入方案资产的子资产；按所在资产判断，不按名称。
        public static bool OwnsInputTimeline(TimelineData timeline) =>
            timeline?.SerializedOwner is TimelineAsset asset &&
            AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(asset)) is TimelinePreviewInputSchemes;

        // 输入 Timeline 或其引用的图改动后经宿主重新读出；返回读出结果是否变化。
        public bool RefreshInputs(ITimelineAssetPreviewProvider provider, IReadOnlyList<GraphHostInputDescriptor> declared)
        {
            TimelineAssetPreviewPreferences current = Current;
            string fingerprint = InputsFingerprint(current.InputTimeline, declared);
            if (fingerprint == m_InputsFingerprint && ReferenceEquals(current, m_ReadScheme))
                return false;
            m_InputsFingerprint = fingerprint;
            m_ReadScheme = current;
            current.InputErrors.Clear();
            current.ConditionPoints = new List<TimelinePreviewConditionPoint>();
            current.Inputs = provider.ReadInputTimeline(current.InputTimeline, current.ConditionPoints, current.InputErrors);
            return true;
        }

        static string InputsFingerprint(TimelineAsset timeline, IReadOnlyList<GraphHostInputDescriptor> declared)
        {
            if (!timeline)
                return string.Empty;
            var builder = new System.Text.StringBuilder();
            builder.Append(timeline.GetInstanceID()).Append(':').Append(EditorUtility.GetDirtyCount(timeline));
            foreach (Track track in timeline.Data.Tracks)
            {
                foreach (Clip clip in track.Clips)
                    if (clip is TreeClip { AssetTree: { } graph })
                        builder.Append('|').Append(graph.GetInstanceID()).Append(':').Append(EditorUtility.GetDirtyCount(graph));
                foreach (TimelineMarker marker in track.Markers)
                    if (marker.Graph)
                        builder.Append('|').Append(marker.Graph.GetInstanceID()).Append(':')
                            .Append(EditorUtility.GetDirtyCount(marker.Graph));
            }
            builder.Append('#').Append(declared.Count);
            return builder.ToString();
        }

        public void Rename(string name)
        {
            Current.Name = name;
            MarkChanged();
            Save();
        }

        public bool DeleteCurrent()
        {
            if (m_Asset == null || m_Asset.Schemes.Count <= 1)
                return false;
            Undo.RegisterCompleteObjectUndo(m_Asset, "删除输入方案");
            int index = Mathf.Clamp(m_Asset.Selected, 0, m_Asset.Schemes.Count - 1);
            if (m_Asset.Schemes[index].InputTimeline)
                Undo.DestroyObjectImmediate(m_Asset.Schemes[index].InputTimeline);
            m_Asset.Schemes.RemoveAt(index);
            m_Asset.Selected = Mathf.Clamp(m_Asset.Selected, 0, m_Asset.Schemes.Count - 1);
            m_Asset.ReleaseUnreferencedGraphs();
            EditorUtility.SetDirty(m_Asset);
            Save();
            return true;
        }

        static TimelinePreviewInputSchemes Find(TimelinePreviewRootIdentity root)
        {
            TimelinePreviewInputSchemes found = null;
            foreach (string guid in AssetDatabase.FindAssets(SearchFilter))
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

        TimelinePreviewInputSchemes Create()
        {
            string path = AssetDatabase.GetAssetPath(m_Source);
            if (path.Length == 0)
                throw new InvalidOperationException($"Timeline '{m_Source.name}' 不在资产中，无法保存输入方案。");
            string directory = System.IO.Path.GetDirectoryName(path)!.Replace('\\', '/');
            var schemes = ScriptableObject.CreateInstance<TimelinePreviewInputSchemes>();
            schemes.Owner = m_Root.Owner;
            schemes.Timeline = m_Root.Timeline;
            AssetDatabase.CreateAsset(schemes,
                AssetDatabase.GenerateUniqueAssetPath($"{directory}/{m_Label}.PreviewInputs.asset"));
            return schemes;
        }
    }
}
