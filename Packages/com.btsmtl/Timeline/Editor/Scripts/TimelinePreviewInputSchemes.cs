using System;
using System.Collections.Generic;
using BTSMTL.Graphs;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    // 一个预览入口的具名输入方案：按入口所属技能与入口 Timeline 的正式作者身份归属，可提交，不参与运行时。
    // 资产文件放在哪里、叫什么都不影响归属；同一 .asset 内的多个 Timeline 子资产各有各的方案。
    public sealed class TimelinePreviewInputSchemes : ScriptableObject
    {
        public string Owner;
        public string Timeline;
        public List<TimelineAssetPreviewPreferences> Schemes = new();
        public int Selected;

        public TimelineAssetPreviewPreferences Current => Schemes[Mathf.Clamp(Selected, 0, Schemes.Count - 1)];
    }

    // 预览入口的正式身份：技能入口为所属技能身份与入口 Timeline 作者身份，独立 Timeline 只用自身作者身份。
    public readonly struct TimelinePreviewEntryIdentity : IEquatable<TimelinePreviewEntryIdentity>
    {
        public TimelinePreviewEntryIdentity(string owner, string timeline)
        {
            if (string.IsNullOrEmpty(timeline))
                throw new ArgumentException("预览入口的 Timeline 缺少作者身份。", nameof(timeline));
            Owner = owner ?? string.Empty;
            Timeline = timeline;
        }

        public string Owner { get; }
        public string Timeline { get; }

        public bool Equals(TimelinePreviewEntryIdentity other) =>
            string.Equals(Owner, other.Owner, StringComparison.Ordinal) &&
            string.Equals(Timeline, other.Timeline, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TimelinePreviewEntryIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Owner, Timeline);
    }

    // 当前预览入口输入方案的唯一存储与操作：第一次改动时在入口 Timeline 旁创建方案资产，此后改动写入该资产；
    // 所有操作只作用于本入口。
    public sealed class TimelinePreviewInputSchemeStore
    {
        const string SearchFilter = "t:" + nameof(TimelinePreviewInputSchemes);
        TimelinePreviewEntryIdentity m_Entry;
        TimelineAsset m_Source;
        TimelinePreviewInputSchemes m_Asset;
        TimelineAssetPreviewPreferences m_Default;
        string m_InputsFingerprint;
        TimelineAssetPreviewPreferences m_ReadScheme;

        public TimelineAssetPreviewPreferences Current =>
            m_Asset != null && m_Asset.Schemes.Count != 0 ? m_Asset.Current : m_Default;

        public IReadOnlyList<TimelineAssetPreviewPreferences> All =>
            m_Asset != null && m_Asset.Schemes.Count != 0 ? m_Asset.Schemes : new[] { m_Default };

        public string Path => m_Asset != null ? AssetDatabase.GetAssetPath(m_Asset) : string.Empty;
        public TimelinePreviewEntryIdentity Entry => m_Entry;

        public void Load(TimelineAsset source, TimelinePreviewEntryIdentity entry)
        {
            Save();
            m_Source = source;
            m_Entry = entry;
            m_Asset = Find(entry);
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
                AssetDatabase.SaveAssetIfDirty(m_Asset);
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
                return current.InputTimeline;
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            timeline.name = current.Name + " 输入";
            AssetDatabase.AddObjectToAsset(timeline, m_Asset);
            current.InputTimeline = timeline;
            EditorUtility.SetDirty(m_Asset);
            Save();
            m_InputsFingerprint = null;
            return timeline;
        }

        // 输入 Timeline 或其引用的图改动后重新读出；返回读出结果是否变化。
        public bool RefreshInputs(IReadOnlyList<GraphHostInputDescriptor> declared)
        {
            TimelineAssetPreviewPreferences current = Current;
            string fingerprint = InputsFingerprint(current.InputTimeline, declared);
            if (fingerprint == m_InputsFingerprint && ReferenceEquals(current, m_ReadScheme))
                return false;
            m_InputsFingerprint = fingerprint;
            m_ReadScheme = current;
            current.InputErrors.Clear();
            current.Inputs = TimelinePreviewInputTimeline.Read(current.InputTimeline, declared, current.InputErrors);
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
            int index = Mathf.Clamp(m_Asset.Selected, 0, m_Asset.Schemes.Count - 1);
            if (m_Asset.Schemes[index].InputTimeline)
                UnityEngine.Object.DestroyImmediate(m_Asset.Schemes[index].InputTimeline, true);
            m_Asset.Schemes.RemoveAt(index);
            m_Asset.Selected = Mathf.Clamp(m_Asset.Selected, 0, m_Asset.Schemes.Count - 1);
            EditorUtility.SetDirty(m_Asset);
            Save();
            return true;
        }

        static TimelinePreviewInputSchemes Find(TimelinePreviewEntryIdentity entry)
        {
            TimelinePreviewInputSchemes found = null;
            foreach (string guid in AssetDatabase.FindAssets(SearchFilter))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset is not TimelinePreviewInputSchemes schemes ||
                        !entry.Equals(new TimelinePreviewEntryIdentity(schemes.Owner, schemes.Timeline)))
                        continue;
                    if (found != null)
                        throw new InvalidOperationException(
                            $"预览入口有两份输入方案：{AssetDatabase.GetAssetPath(found)} 与 {path}；请删除其中一份。");
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
            schemes.Owner = m_Entry.Owner;
            schemes.Timeline = m_Entry.Timeline;
            AssetDatabase.CreateAsset(schemes,
                AssetDatabase.GenerateUniqueAssetPath($"{directory}/{m_Source.name}.PreviewInputs.asset"));
            return schemes;
        }
    }
}
