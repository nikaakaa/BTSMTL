#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using BTSMTL.Orchestration;
using Slate;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    // 路线视图的一组：Asset 为空表示还没有输入 Timeline 的预览输入组。
    // 时间换算：全局 = 本地 + Offset；[PlayedStart, PlayedEnd] 为这次播放实际走过的全局区间。
    // Describe 给出元素的显示文字（图、起点 → 文字），为空时用原名称；预览输入组用它显示推导或执行证据。
    // DrawHeader 在组头标题右侧绘制附加控件（IMGUI），预览输入组用它放方案与条件。
    public readonly struct TimelineRouteGroup
    {
        public TimelineRouteGroup(TimelineAsset asset, string label, float offset, float playedStart, float playedEnd,
            Action open, Func<ScriptableObject, OrchestrationTime, string> describe = null, Action<Rect> drawHeader = null)
        {
            Asset = asset;
            Label = label ?? string.Empty;
            Offset = offset;
            PlayedStart = playedStart;
            PlayedEnd = playedEnd;
            Open = open;
            Describe = describe;
            DrawHeader = drawHeader;
        }

        public TimelineAsset Asset { get; }
        public string Label { get; }
        public float Offset { get; }
        public float PlayedStart { get; }
        public float PlayedEnd { get; }
        public Action Open { get; }
        public Func<ScriptableObject, OrchestrationTime, string> Describe { get; }
        public Action<Rect> DrawHeader { get; }
    }

    // 预览路线视图：输入 Timeline 与路线上每次播放各成一组，摆在预览全局时刻上。
    // 每份 Timeline 只有一个原 Slate 绑定，编辑、Undo 与作者宿主都由它负责；本类只换算时间并转发。
    sealed class TimelineRouteBinding : IEmbeddedTimelineBinding
    {
        sealed class Source
        {
            internal TimelineAsset Asset;
            internal TimelineEditorSessionContext Session;
            internal BtsmtlSlateTimelineBinding Binding;
            internal int BuiltVersion = -1;
        }

        readonly TimelineContractCatalog m_Contracts;
        readonly ITimelineEditorAuthoringHost m_Host;
        readonly Action<Clip> m_OpenSourceClip;
        readonly Func<TimelineAsset> m_CreateInput;
        readonly Dictionary<TimelineAsset, Source> m_Sources = new();
        readonly List<RouteGroup> m_Groups = new();
        readonly List<TimelineRouteGroup> m_Specs = new();
        IEmbeddedTimelineElementBinding m_Selected;
        (int Group, string Id) m_SelectedKey;
        float m_Length = 1f;
        float m_CurrentTime;
        float m_ViewTimeMin;
        float m_ViewTimeMax = 1f;
        bool m_ReadOnly;
        bool m_DisplayFrames;

        internal TimelineRouteBinding(TimelineContractCatalog contracts, ITimelineEditorAuthoringHost host,
            Action<Clip> openSourceClip, Func<TimelineAsset> createInput)
        {
            m_Contracts = contracts;
            m_Host = host;
            m_OpenSourceClip = openSourceClip;
            m_CreateInput = createInput;
        }

        public event Action RepaintRequested;
        public event Action<string> AuthoringIssue;
        // 分组或包装对象换新后通知 Slate 清理按对象缓存的绘制数据。
        public event Action Rebuilt;

        internal void Configure(IReadOnlyList<TimelineRouteGroup> groups, float length,
            IReadOnlyList<EmbeddedTimelinePreviewMark> marks, bool readOnly)
        {
            bool firstConfigure = m_Specs.Count == 0;
            m_Specs.Clear();
            m_Specs.AddRange(groups);
            m_Length = Mathf.Max(0.001f, length);
            m_ReadOnly = readOnly;
            PreviewPath = new EmbeddedTimelinePreviewPath(string.Empty, new[] { new Vector2(0f, m_Length) }, marks);
            var used = new HashSet<TimelineAsset>();
            foreach (TimelineRouteGroup spec in m_Specs)
                if (spec.Asset)
                {
                    used.Add(spec.Asset);
                    if (!m_Sources.ContainsKey(spec.Asset))
                        m_Sources.Add(spec.Asset, CreateSource(spec.Asset));
                }
            foreach (TimelineAsset removed in m_Sources.Keys.Where(asset => !used.Contains(asset)).ToArray())
            {
                DisposeSource(m_Sources[removed]);
                m_Sources.Remove(removed);
            }
            if (firstConfigure)
                m_ViewTimeMax = m_Length;
            m_CurrentTime = Mathf.Clamp(m_CurrentTime, 0f, m_Length);
            RebuildGroups();
        }

        Source CreateSource(TimelineAsset asset)
        {
            var request = new TimelineEditorOpenRequest(asset.Data, asset, "m_Data",
                AssetDatabase.IsSubAsset(asset) ? "Private Asset" : "Shared Asset", null, m_Contracts,
                m_Host);
            var source = new Source { Asset = asset, Session = new TimelineEditorSessionContext(request) };
            source.Binding = new BtsmtlSlateTimelineBinding(request, source.Session, m_OpenSourceClip);
            source.Binding.RepaintRequested += Repaint;
            source.Binding.AuthoringIssue += ReportIssue;
            asset.Data.OnValueChanged += OnSourceChanged;
            return source;
        }

        void DisposeSource(Source source)
        {
            source.Binding.CancelEdit();
            source.Binding.RepaintRequested -= Repaint;
            source.Binding.AuthoringIssue -= ReportIssue;
            source.Binding.ReleaseResources();
            if (source.Asset)
                source.Asset.Data.OnValueChanged -= OnSourceChanged;
            source.Session.Dispose();
        }

        internal void Dispose()
        {
            foreach (Source source in m_Sources.Values)
                DisposeSource(source);
            m_Sources.Clear();
            m_Groups.Clear();
            m_Selected = null;
        }

        // 外部修改（Undo、其它窗口、推导重建前的作者改动）后，各份 Timeline 的绑定重读原资产。
        void OnSourceChanged()
        {
            foreach (Source source in m_Sources.Values)
                if (!source.Binding.IsCommittingEdit && !source.Binding.IsSourceCurrent())
                    source.Binding.Rebuild();
            Repaint();
        }

        internal void RebuildFromSources()
        {
            foreach (Source source in m_Sources.Values)
                source.Binding.Rebuild();
            RebuildGroups();
        }

        void EnsureCurrent()
        {
            foreach (Source source in m_Sources.Values)
                if (source.BuiltVersion != source.Binding.BuildVersion)
                {
                    RebuildGroups();
                    return;
                }
        }

        void RebuildGroups()
        {
            var collapsed = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (RouteGroup group in m_Groups)
                collapsed[group.AuthoringId] = group.IsCollapsed;
            m_Groups.Clear();
            for (int index = 0; index < m_Specs.Count; index++)
            {
                TimelineRouteGroup spec = m_Specs[index];
                Source source = spec.Asset ? m_Sources[spec.Asset] : null;
                var group = new RouteGroup(this, index, spec, source);
                if (collapsed.TryGetValue(group.AuthoringId, out bool wasCollapsed))
                    group.IsCollapsed = wasCollapsed;
                m_Groups.Add(group);
            }
            foreach (Source source in m_Sources.Values)
                source.BuiltVersion = source.Binding.BuildVersion;
            m_Selected = FindSelected();
            Rebuilt?.Invoke();
            Repaint();
        }

        IEmbeddedTimelineElementBinding FindSelected()
        {
            if (m_SelectedKey.Id == null || m_SelectedKey.Group < 0 || m_SelectedKey.Group >= m_Groups.Count)
                return null;
            RouteGroup group = m_Groups[m_SelectedKey.Group];
            if (group.Inner?.AuthoringId == m_SelectedKey.Id)
                return group;
            foreach (RouteTrack track in group.RouteTracks)
            {
                if (track.Inner.AuthoringId == m_SelectedKey.Id)
                    return track;
                foreach (RouteClip clip in track.RouteClips)
                    if (clip.Inner.AuthoringId == m_SelectedKey.Id)
                        return clip;
                foreach (RouteMarker marker in track.RouteMarkers)
                    if (marker.Inner.AuthoringId == m_SelectedKey.Id)
                        return marker;
            }
            return null;
        }

        void Repaint() => RepaintRequested?.Invoke();
        void ReportIssue(string message) => AuthoringIssue?.Invoke(message);

        Source Primary
        {
            get
            {
                foreach (TimelineRouteGroup spec in m_Specs)
                    if (spec.Asset && m_Sources.TryGetValue(spec.Asset, out Source source) &&
                        !TimelinePreviewInputSchemeStore.OwnsInputTimeline(spec.Asset.Data))
                        return source;
                return m_Sources.Values.FirstOrDefault();
            }
        }

        public string DisplayName => "预览路线";
        public int FrameRate => Primary?.Binding.FrameRate ?? TimelineUtility.FrameRate;
        public float SnapTime(float time) => Primary?.Binding.SnapTime(time) ?? Mathf.Max(0f, time);
        public string SnapLabel => Primary?.Binding.SnapLabel ?? "吸附 关闭";
        public void ShowSnapSettings(Rect rect) => Primary?.Binding.ShowSnapSettings(rect);
        public float Length => m_Length;
        public float CurrentTime
        {
            get => m_CurrentTime;
            set
            {
                float next = Mathf.Clamp(value, 0f, m_Length);
                if (next == m_CurrentTime)
                    return;
                m_CurrentTime = next;
                Repaint();
            }
        }
        public bool DisplayFrames
        {
            get => m_DisplayFrames;
            set
            {
                m_DisplayFrames = value;
                foreach (Source source in m_Sources.Values)
                    source.Binding.DisplayFrames = value;
            }
        }
        public float SnapInterval => Primary?.Binding.SnapInterval ?? 0f;
        public float StepInterval => Primary?.Binding.StepInterval ?? 0.01f;
        public float ViewTimeMin
        {
            get => m_ViewTimeMin;
            set { m_ViewTimeMin = Mathf.Min(value, m_ViewTimeMax - 0.001f); Repaint(); }
        }
        public float ViewTimeMax
        {
            get => m_ViewTimeMax;
            set { m_ViewTimeMax = Mathf.Max(value, m_ViewTimeMin + 0.001f); Repaint(); }
        }
        public bool IsReadOnly => m_ReadOnly;
        public bool ClipAdjustRangeOnly => false;
        public bool ResourceEditing => false;
        public IReadOnlyList<IEmbeddedTimelineGroupBinding> Groups
        {
            get
            {
                EnsureCurrent();
                return m_Groups;
            }
        }
        public IEmbeddedTimelineElementBinding Selected => m_Selected;
        public EmbeddedTimelinePreviewPath PreviewPath { get; private set; }
        public bool CanPasteClip => m_Sources.Values.Any(source => source.Binding.CanPasteClip);

        public bool TryGetTrack(string authoringId, out IEmbeddedTimelineTrackBinding track)
        {
            track = Groups.SelectMany(group => ((RouteGroup)group).RouteTracks)
                .FirstOrDefault(value => value.AuthoringId == authoringId);
            return track != null;
        }

        public bool TryGetClip(string authoringId, out IEmbeddedTimelineClipBinding clip)
        {
            clip = Groups.SelectMany(group => ((RouteGroup)group).RouteTracks).SelectMany(track => track.RouteClips)
                .FirstOrDefault(value => value.AuthoringId == authoringId);
            return clip != null;
        }

        public void Select(IEmbeddedTimelineElementBinding element)
        {
            (RouteGroup group, IEmbeddedTimelineElementBinding inner) = Unwrap(element);
            Source owner = group?.Source;
            foreach (Source source in m_Sources.Values)
                if (source != owner && source.Binding.Selected != null)
                    source.Binding.Select(null);
            owner?.Binding.Select(inner);
            m_Selected = element;
            m_SelectedKey = group == null ? (-1, null) : (group.Index, inner?.AuthoringId);
            Repaint();
        }

        static (RouteGroup Group, IEmbeddedTimelineElementBinding Inner) Unwrap(IEmbeddedTimelineElementBinding element) =>
            element switch
            {
                RouteGroup group => (group, group.Inner),
                RouteTrack track => (track.Group, track.Inner),
                RouteClip clip => (clip.RouteTrack.Group, clip.Inner),
                RouteMarker marker => (marker.RouteTrack.Group, marker.Inner),
                _ => (null, null)
            };

        RouteGroup EditGroup()
        {
            (RouteGroup group, _) = Unwrap(m_Selected);
            return group ?? (m_Groups.Count != 0 ? m_Groups[0] : null);
        }

        public void AddTrack() => EditGroup()?.AddTrack();

        void AddTrackTo(RouteGroup group)
        {
            if (m_ReadOnly)
                return;
            if (group.Source != null)
            {
                group.Source.Binding.AddTrack();
                return;
            }
            // 还没有输入 Timeline：先在方案中创建，重新分组后在新组里加轨道。
            TimelineAsset created = m_CreateInput?.Invoke();
            if (!created)
                return;
            TimelineRouteGroup spec = m_Specs[group.Index];
            m_Specs[group.Index] = new TimelineRouteGroup(created, spec.Label, spec.Offset, spec.PlayedStart, spec.PlayedEnd,
                null, spec.Describe, spec.DrawHeader);
            Configure(m_Specs.ToArray(), m_Length, PreviewPath.Marks, m_ReadOnly);
            m_Groups[group.Index].Source?.Binding.AddTrack();
        }

        public void AddClip(IEmbeddedTimelineTrackBinding track, double time)
        {
            if (track is RouteTrack route)
                route.Owner.AddClip(route.Inner, time - route.Offset);
        }

        public void AddMarker(IEmbeddedTimelineTrackBinding track, double time)
        {
            if (track is RouteTrack route)
                route.Owner.AddMarker(route.Inner, time - route.Offset);
        }

        public void DeleteMarker(IEmbeddedTimelineMarkerBinding marker)
        {
            if (marker is RouteMarker route)
                route.Owner.DeleteMarker(route.Inner);
        }

        public void MoveMarker(IEmbeddedTimelineMarkerBinding marker, double time)
        {
            if (marker is RouteMarker route)
                route.Owner.MoveMarker(route.Inner, time - route.RouteTrack.Offset);
        }

        public void OpenMarker(IEmbeddedTimelineMarkerBinding marker)
        {
            if (marker is RouteMarker route)
                route.Owner.OpenMarker(route.Inner);
        }

        public void SetTrackActive(IEmbeddedTimelineTrackBinding track, bool active)
        {
            if (track is RouteTrack route)
                route.Owner.SetTrackActive(route.Inner, active);
        }

        public void DeleteTrack(IEmbeddedTimelineTrackBinding track)
        {
            if (track is RouteTrack route)
                route.Owner.DeleteTrack(route.Inner);
        }

        public void DeleteClip(IEmbeddedTimelineClipBinding clip)
        {
            if (clip is RouteClip route)
                route.Owner.DeleteClip(route.Inner);
        }

        public void DeleteClips(IReadOnlyList<IEmbeddedTimelineClipBinding> clips)
        {
            foreach (var owned in clips.OfType<RouteClip>().GroupBy(clip => clip.Owner))
                owned.Key.DeleteClips(owned.Select(clip => clip.Inner).Distinct().ToArray());
        }

        public void SplitClip(IEmbeddedTimelineClipBinding clip, double time)
        {
            if (clip is RouteClip route)
                route.Owner.SplitClip(route.Inner, time - route.RouteTrack.Offset);
        }

        public void MoveTrack(IEmbeddedTimelineTrackBinding track, int index)
        {
            if (track is RouteTrack route)
                route.Owner.MoveTrack(route.Inner, index);
        }

        // 复制的 Clip 交给每份 Timeline 的绑定，可粘贴到任一组。
        public void CopyClip(IEmbeddedTimelineClipBinding clip)
        {
            if (clip is not RouteClip route)
                return;
            foreach (Source source in m_Sources.Values)
                source.Binding.CopyClip(route.Inner);
        }

        public void PasteClip(IEmbeddedTimelineTrackBinding track, double time)
        {
            if (track is RouteTrack route)
                route.Owner.PasteClip(route.Inner, time - route.Offset);
        }

        public Action CreateSourceNavigation(IEmbeddedTimelineClipBinding clip) =>
            clip is RouteClip route ? route.Owner.CreateSourceNavigation(route.Inner) : null;

        // Slate 的编辑事务不带元素，交给每份 Timeline 的绑定；没有改动的绑定提交时什么也不做。
        public void BeginEdit(string undoName, EmbeddedTimelineEditKind kind = EmbeddedTimelineEditKind.Content)
        {
            if (m_ReadOnly)
                return;
            foreach (Source source in m_Sources.Values)
                source.Binding.BeginEdit(undoName, kind);
        }

        public void CommitEdit(long inputStartedAt)
        {
            foreach (Source source in m_Sources.Values.ToArray())
                source.Binding.CommitEdit(inputStartedAt);
        }

        public void CancelEdit()
        {
            foreach (Source source in m_Sources.Values.ToArray())
                source.Binding.CancelEdit();
        }

        public void RequestRepaint(long inputStartedAt) => Repaint();

        sealed class RouteGroup : IEmbeddedTimelineGroupBinding, IEmbeddedTimelineGroupCommands, IEmbeddedTimelineGroupPlayback,
            IEmbeddedTimelineGroupHeader
        {
            readonly TimelineRouteBinding m_Owner;
            readonly TimelineRouteGroup m_Spec;
            readonly List<IEmbeddedTimelineTrackBinding> m_Tracks = new();
            internal readonly List<RouteTrack> RouteTracks = new();

            internal RouteGroup(TimelineRouteBinding owner, int index, TimelineRouteGroup spec, Source source)
            {
                m_Owner = owner;
                m_Spec = spec;
                Index = index;
                Source = source;
                Inner = source?.Binding.Groups.Count > 0 ? source.Binding.Groups[0] : null;
                AuthoringId = $"{index}:{(spec.Asset ? spec.Asset.Data.AuthoringId : "input")}";
                if (Inner == null)
                    return;
                foreach (IEmbeddedTimelineTrackBinding track in Inner.Tracks)
                {
                    var route = new RouteTrack(this, track);
                    RouteTracks.Add(route);
                    m_Tracks.Add(route);
                }
            }

            internal int Index { get; }
            internal Source Source { get; }
            internal IEmbeddedTimelineGroupBinding Inner { get; }
            internal float Offset => m_Spec.Offset;
            internal Func<ScriptableObject, OrchestrationTime, string> Describe => m_Spec.Describe;
            public string AuthoringId { get; }
            public string DisplayName => m_Spec.Label;
            public bool IsLocked { get; set; }
            public bool IsActive { get; set; } = true;
            public bool IsCollapsed { get; set; }
            public IReadOnlyList<IEmbeddedTimelineTrackBinding> Tracks => m_Tracks;
            public float PlayedStart => m_Spec.PlayedStart;
            public float PlayedEnd => m_Spec.PlayedEnd;
            public bool CanOpen => m_Spec.Open != null;
            public void AddTrack() => m_Owner.AddTrackTo(this);
            public void Open() => m_Spec.Open?.Invoke();
            public void DrawHeader(Rect rect) => m_Spec.DrawHeader?.Invoke(rect);
            internal string Prefix => AuthoringId + "/";
        }

        sealed class RouteTrack : IEmbeddedTimelineTrackBinding, IEmbeddedTimelineMarkerTrackBinding
        {
            readonly List<IEmbeddedTimelineClipBinding> m_Clips = new();
            readonly List<IEmbeddedTimelineMarkerBinding> m_Markers = new();
            internal readonly List<RouteClip> RouteClips = new();
            internal readonly List<RouteMarker> RouteMarkers = new();

            internal RouteTrack(RouteGroup group, IEmbeddedTimelineTrackBinding inner)
            {
                Group = group;
                Inner = inner;
                AuthoringId = group.Prefix + inner.AuthoringId;
                foreach (IEmbeddedTimelineClipBinding clip in inner.Clips)
                {
                    var route = new RouteClip(this, clip);
                    RouteClips.Add(route);
                    m_Clips.Add(route);
                }
                if (inner is IEmbeddedTimelineMarkerTrackBinding markers)
                    foreach (IEmbeddedTimelineMarkerBinding marker in markers.Markers)
                    {
                        var route = new RouteMarker(this, marker);
                        RouteMarkers.Add(route);
                        m_Markers.Add(route);
                    }
            }

            internal RouteGroup Group { get; }
            internal IEmbeddedTimelineTrackBinding Inner { get; }
            internal BtsmtlSlateTimelineBinding Owner => Group.Source.Binding;
            internal float Offset => Group.Offset;
            public string AuthoringId { get; }
            public string DisplayName => Inner.DisplayName;
            public bool IsLocked { get => Inner.IsLocked; set => Inner.IsLocked = value; }
            public bool IsActive { get => Inner.IsActive; set => Inner.IsActive = value; }
            public bool ShowCurves { get => Inner.ShowCurves; set => Inner.ShowCurves = value; }
            public float CustomHeight { get => Inner.CustomHeight; set => Inner.CustomHeight = value; }
            public Color Color => Inner.Color;
            public float StartTime => Inner.StartTime + Offset;
            public float EndTime => Inner.EndTime + Offset;
            public float DefaultHeight => Inner.DefaultHeight;
            public float FinalHeight => Inner.FinalHeight;
            public float GetFinalHeight(string inspectedParameterId) => Inner.GetFinalHeight(inspectedParameterId);
            public IReadOnlyList<IEmbeddedTimelineClipBinding> Clips => m_Clips;
            public IEmbeddedTimelineClipBinding SelectedClip =>
                RouteClips.FirstOrDefault(clip => ReferenceEquals(clip.Inner, Inner.SelectedClip));
            public IReadOnlyList<IEmbeddedTimelineMarkerBinding> Markers => m_Markers;
        }

        sealed class RouteMarker : IEmbeddedTimelineMarkerBinding
        {
            internal RouteMarker(RouteTrack track, IEmbeddedTimelineMarkerBinding inner)
            {
                RouteTrack = track;
                Inner = inner;
                AuthoringId = track.Group.Prefix + inner.AuthoringId;
            }

            internal RouteTrack RouteTrack { get; }
            internal IEmbeddedTimelineMarkerBinding Inner { get; }
            internal BtsmtlSlateTimelineBinding Owner => RouteTrack.Owner;
            public IEmbeddedTimelineTrackBinding Track => RouteTrack;
            public double Time => Inner.Time + RouteTrack.Offset;
            public string AuthoringId { get; }
            public string DisplayName => RouteTrack.Group.Describe is { } describe &&
                BtsmtlSlateTimelineBinding.SourceOf(Inner) is { } source
                    ? describe(source.Graph, source.Time) : Inner.DisplayName;
            public bool IsLocked { get => Inner.IsLocked; set => Inner.IsLocked = value; }
        }

        sealed class RouteClip : IEmbeddedTimelineClipBinding, IEmbeddedTimelineSourceRangeBinding
        {
            internal RouteClip(RouteTrack track, IEmbeddedTimelineClipBinding inner)
            {
                RouteTrack = track;
                Inner = inner;
                AuthoringId = track.Group.Prefix + inner.AuthoringId;
            }

            internal RouteTrack RouteTrack { get; }
            internal IEmbeddedTimelineClipBinding Inner { get; }
            internal BtsmtlSlateTimelineBinding Owner => RouteTrack.Owner;
            float Offset => RouteTrack.Offset;
            public IEmbeddedTimelineTrackBinding Track => RouteTrack;
            public string AuthoringId { get; }
            public string DisplayName => Inner.DisplayName;
            public bool IsLocked { get => Inner.IsLocked; set => Inner.IsLocked = value; }
            public string Info => RouteTrack.Group.Describe is { } describe &&
                BtsmtlSlateTimelineBinding.SourceOf(Inner) is TreeClip tree
                    ? describe(tree.AssetTree, tree.StartTime) : Inner.Info;
            public bool IsActive => Inner.IsActive;
            public bool IsTimeQuantized => Inner.IsTimeQuantized;
            public bool IsValid => Inner.IsValid;
            public bool IsCollapsed { get => Inner.IsCollapsed; set => Inner.IsCollapsed = value; }
            public float StartTime { get => Inner.StartTime + Offset; set => Inner.StartTime = value - Offset; }
            public float EndTime { get => Inner.EndTime + Offset; set => Inner.EndTime = value - Offset; }
            public float Length => Inner.Length;
            public float BlendIn { get => Inner.BlendIn; set => Inner.BlendIn = value; }
            public float BlendOut { get => Inner.BlendOut; set => Inner.BlendOut = value; }
            public bool CanScale => Inner.CanScale;
            public bool CanClipIn => Inner.CanClipIn;
            public double ClipInTime { get => Inner.ClipInTime; set => Inner.ClipInTime = value; }
            public bool CanBlendIn => Inner.CanBlendIn;
            public bool CanBlendOut => Inner.CanBlendOut;
            public ulong CurveRevision => Inner.CurveRevision;
            public IReadOnlyList<IEmbeddedTimelineParameterBinding> Parameters => Inner.Parameters;
            public IReadOnlyList<IEmbeddedTimelineCurveBinding> Curves => Inner.Curves;
            public bool CanCrossBlend(IEmbeddedTimelineClipBinding other) =>
                other is RouteClip route && route.Owner == Owner && Inner.CanCrossBlend(route.Inner);
            public void AddIdentityKey(float time) => Inner.AddIdentityKey(time);
            public void Split(float time) => Inner.Split(time - Offset);
            public void StretchFit() => Inner.StretchFit();
            public void CleanKeysOffRange() => Inner.CleanKeysOffRange();
            public void ResetAnimation() => Inner.ResetAnimation();

            public void AdjustSourceRange(double originalStartTime, double currentStartTime, double currentEndTime, bool trimStart)
            {
                if (Inner is IEmbeddedTimelineSourceRangeBinding range)
                    range.AdjustSourceRange(originalStartTime - Offset, currentStartTime - Offset, currentEndTime - Offset,
                        trimStart);
            }
        }
    }
}
#endif
