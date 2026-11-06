using System.Collections.Generic;
using System.Threading.Tasks;
using BTSMTL.Authoring.Graph;
using BTSMTL.Graphs;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    public interface ITimelinePreviewInputSchemeStorage
    {
        TimelinePreviewInputSchemes Find(TimelinePreviewRootIdentity root);
        void AddSchemes(TimelineAsset entry, string label, TimelinePreviewInputSchemes schemes);
        void AddInputTimeline(TimelinePreviewInputSchemes schemes, TimelineAsset timeline);
        void RemoveInputTimeline(TimelineAsset timeline);
        void ReleaseUnreferencedGraphs(TimelinePreviewInputSchemes schemes);
        string InputPath(TimelinePreviewInputSchemes schemes);
        bool OwnsInputTimeline(TimelineData timeline);
    }

    // 一个预览根的具名输入方案：编排根按根程序的正式身份归属（Owner），独立 Timeline 按自身作者身份归属（Timeline）；
    // 可提交，不参与运行时。正式文档放在哪里、叫什么都不影响归属。
    public sealed partial class TimelinePreviewInputSchemes : ScriptableObject
    {
        [System.NonSerialized] ITimelineAuthoringDocument m_AuthoringDocument;
        public ITimelineAuthoringDocument AuthoringDocument => m_AuthoringDocument;
        public void BindAuthoringDocument(ITimelineAuthoringDocument document) => m_AuthoringDocument = document;
        public TimelineAssetPreviewPreferences Current => Schemes[Mathf.Clamp(Selected, 0, Schemes.Count - 1)];
    }

    // 当前预览根输入方案的唯一操作入口；正式文档提供存储，所有操作只作用于本根。
    public sealed class TimelinePreviewInputSchemeStore
    {
        TimelinePreviewRootIdentity m_Root;
        TimelineAsset m_Source;
        string m_Label;
        ITimelinePreviewInputSchemeStorage m_Storage;
        TimelinePreviewInputSchemes m_Asset;
        TimelineAssetPreviewPreferences m_Default;
        string m_InputsFingerprint;
        TimelineAssetPreviewPreferences m_ReadScheme;
        int? m_Selection;

        public bool SavesChanges { get; private set; } = true;
        public int SelectedIndex => m_Selection ??
            (m_Asset != null && m_Asset.Schemes.Count != 0
                ? Mathf.Clamp(m_Asset.Selected, 0, m_Asset.Schemes.Count - 1) : 0);

        TimelineAssetPreviewPreferences AuthoredCurrent =>
            m_Asset != null && m_Asset.Schemes.Count != 0
                ? m_Selection is { } index ? m_Asset.Schemes[index] : m_Asset.Current
                : m_Default;

        public TimelineAssetPreviewPreferences Current => SavesChanges ? AuthoredCurrent : m_Default;

        public IReadOnlyList<TimelineAssetPreviewPreferences> All =>
            m_Asset != null && m_Asset.Schemes.Count != 0 ? m_Asset.Schemes : new[] { m_Default };

        public string Path => m_Asset != null ? m_Storage.InputPath(m_Asset) : string.Empty;
        public TimelinePreviewInputSchemes Document => m_Asset;
        public TimelinePreviewRootIdentity Root => m_Root;

        public void Load(TimelinePreviewRoot root, bool saveChanges = true, int? selection = null,
            TimelinePreviewInputSchemes inputDocument = null)
        {
            m_Source = root.Entry;
            m_Label = root.Label;
            m_Root = root.Identity;
            m_Storage = (ITimelinePreviewInputSchemeStorage)(inputDocument != null
                ? inputDocument.AuthoringDocument : root.Entry.Data.AuthoringDocument);
            m_Asset = inputDocument != null ? inputDocument : m_Storage.Find(root.Identity);
            if (selection is { } index && (index < 0 || index >= All.Count))
                throw new System.ArgumentOutOfRangeException(nameof(selection), "所选预览输入方案不存在。");
            SavesChanges = saveChanges;
            m_Selection = selection;
            if (m_Asset != null)
                foreach (TimelineAssetPreviewPreferences scheme in m_Asset.Schemes)
                    if (scheme.InputTimeline)
                        TimelineAuthoringGraphBindings.Prepare(scheme.InputTimeline.Data);
            m_Default = new TimelineAssetPreviewPreferences();
            if (!saveChanges)
                JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(AuthoredCurrent), m_Default);
            m_InputsFingerprint = null;
            m_ReadScheme = null;
        }

        // 当前方案已被修改：尚无方案时由正式文档创建，并标记待保存。
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

        public async Task Save()
        {
            if (SavesChanges && m_Asset != null)
                await ((ITimelineAuthoringDocument)m_Storage).Save();
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
        public async Task SaveAs(string name)
        {
            TimelineAssetPreviewPreferences copy =
                JsonUtility.FromJson<TimelineAssetPreviewPreferences>(JsonUtility.ToJson(Current));
            copy.Name = name;
            copy.AuthoringId = System.Guid.NewGuid().ToString("D");
            MarkChanged();
            if (Current.InputTimeline)
            {
                copy.InputTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
                copy.InputTimeline.SetData(Current.InputTimeline.Data.Clone());
                copy.InputTimeline.ConfigureAuthoringScope(Current.InputTimeline.AuthoringScope);
                copy.InputTimeline.Data.RegenerateAuthoringIdentities();
                copy.InputTimeline.name = name + " 输入";
                m_Storage.AddInputTimeline(m_Asset, copy.InputTimeline);
                TimelineAuthoringGraphBindings.Prepare(copy.InputTimeline.Data);
            }
            m_Asset.Schemes.Add(copy);
            m_Asset.Selected = m_Asset.Schemes.Count - 1;
            m_InputsFingerprint = null;
            await Save();
        }

        // 当前方案的输入 Timeline；尚无时加入所属正式文档。
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
            timeline.SetData(TimelineData.CreateDefault(null));
            timeline.name = current.Name + " 输入";
            m_Storage.AddInputTimeline(m_Asset, timeline);
            TimelineAuthoringGraphBindings.Prepare(timeline.Data);
            current.InputTimeline = timeline;
            EditorUtility.SetDirty(m_Asset);
            m_InputsFingerprint = null;
            return timeline;
        }

        public static bool OwnsInputTimeline(TimelineData timeline) =>
            timeline?.AuthoringDocument is ITimelinePreviewInputSchemeStorage storage && storage.OwnsInputTimeline(timeline);

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

        public async Task Rename(string name)
        {
            Current.Name = name;
            MarkChanged();
            await Save();
        }

        public async Task<bool> DeleteCurrent()
        {
            if (m_Asset == null || m_Asset.Schemes.Count <= 1)
                return false;
            Undo.RegisterCompleteObjectUndo(m_Asset, "删除输入方案");
            int index = Mathf.Clamp(m_Asset.Selected, 0, m_Asset.Schemes.Count - 1);
            if (m_Asset.Schemes[index].InputTimeline)
                m_Storage.RemoveInputTimeline(m_Asset.Schemes[index].InputTimeline);
            m_Asset.Schemes.RemoveAt(index);
            m_Asset.Selected = Mathf.Clamp(m_Asset.Selected, 0, m_Asset.Schemes.Count - 1);
            m_Storage.ReleaseUnreferencedGraphs(m_Asset);
            EditorUtility.SetDirty(m_Asset);
            await Save();
            return true;
        }

        TimelinePreviewInputSchemes Create()
        {
            var schemes = TimelinePreviewInputSchemes.CreateDefault(m_Root.Owner, m_Root.Timeline, m_Default);
            m_Storage.AddSchemes(m_Source, m_Label, schemes);
            return schemes;
        }
    }
}
