using System;
using System.Collections;
using System.Collections.Generic;
using BTSMTL.Diagnostics;
using BTSMTL.Diagnostics.Editor;
using Slate;
using Unity.Profiling;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace BTSMTL.Timeline.Editor
{
    public sealed class BtsmtlExecutionTimelineView : VisualElement, IDisposable
    {
        public const RuntimeTraceChannel TraceChannels = RuntimeTraceChannel.Graph |
            RuntimeTraceChannel.StateMachine | RuntimeTraceChannel.Timeline;
        static readonly ProfilerMarker s_Projection = new("RuntimeDebug.ExecutionProjection");
        static readonly ProfilerMarker s_Draw = new("RuntimeDebug.ExecutionDraw");
        readonly RuntimeDebugSession m_Session = RuntimeDebugSession.Shared;
        readonly IMGUIContainer m_Surface;
        readonly Action m_BeginWindows;
        readonly Action m_EndWindows;
        readonly Action<RuntimeExecutionSpan> m_OpenSource;
        readonly Action<float> m_SeekPreview;
        readonly Func<bool> m_CanSeekPreview;
        readonly Func<float> m_PreviewTime;
        CutsceneEditorSurface m_Slate;
        ExecutionBinding m_Binding;
        long m_Version = -1;
        long m_TargetRevision = -1;
        long m_ProjectionVersion = -1;
        bool m_Visible;
        Guid m_LastCaptureId;
        RuntimeDebugAttachmentState m_LastState;
        RuntimeExecutionSpan? m_SeekSelection;

        public BtsmtlExecutionTimelineView(Action beginWindows, Action endWindows,
            Action<RuntimeExecutionSpan> openSource, Action<float> seekPreview,
            Func<bool> canSeekPreview, Func<float> previewTime)
        {
            m_BeginWindows = beginWindows;
            m_EndWindows = endWindows;
            m_OpenSource = openSource;
            m_SeekPreview = seekPreview;
            m_CanSeekPreview = canSeekPreview;
            m_PreviewTime = previewTime;
            name = "workbench-execution-timeline";
            style.flexGrow = 1;
            style.minHeight = 140;
            m_Surface = new IMGUIContainer(Draw) { name = "slate-execution-surface" };
            m_Surface.style.flexGrow = 1;
            Add(m_Surface);
            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                m_Session.Changed += RefreshProjection;
                m_Session.HistoryCursorChanged += RefreshLiveCursor;
                EditorApplication.update += RefreshLiveCursor;
                RefreshProjection();
            });
            RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                m_Session.Changed -= RefreshProjection;
                m_Session.HistoryCursorChanged -= RefreshLiveCursor;
                EditorApplication.update -= RefreshLiveCursor;
            });
        }

        public void SetVisible(bool visible)
        {
            m_Visible = visible;
            style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible)
                RefreshProjection();
            else
                ReleaseSurface();
        }

        void RefreshLiveCursor()
        {
            if (!m_Visible)
                return;
            if (m_Binding == null && m_CanSeekPreview())
                RefreshProjection();
            if (m_Binding != null && m_CanSeekPreview())
            {
                m_Binding.RefreshTimeRange(false);
                m_Binding.SetCursor(m_PreviewTime());
            }
        }

        void RefreshProjection()
        {
            if (!m_Visible || panel == null)
                return;
            if (m_ProjectionVersion == m_Session.ProjectionVersion &&
                m_Version == m_Session.CaptureVersion && m_TargetRevision == m_Session.TargetRevision &&
                m_LastCaptureId == m_Session.CaptureId && m_LastState == m_Session.AttachmentState)
            {
                if (m_Binding != null)
                    m_Binding.SetCursor(m_PreviewTime());
                return;
            }
            using var profile = s_Projection.Auto();
            RuntimeExecutionTimeline timeline = m_Session.BuildExecutionTimeline();
            if (timeline == null)
            {
                if (m_Binding?.Timeline != null)
                    ClearProjection();
                if (m_CanSeekPreview())
                    EnsureSurface();
                else
                    ClearProjection();
                return;
            }
            m_Session.GetExecutionAuthoringCapacity(out int timelineCapacity, out int trackCapacity);
            EnsureSurface();
            m_Binding.ReserveAuthoringCapacity(timelineCapacity, trackCapacity);
            if (m_Binding.CaptureId != timeline.CaptureId)
            {
                ClearSelection();
                m_Binding.Reset(m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Logic),
                    m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Presentation));
                m_Slate.EmbeddedScrollPosition = Vector2.zero;
            }
            bool structureReset = m_Binding.Apply(timeline, m_Session.IsFollowingExecution, m_SeekSelection?.StartSequence);
            RuntimeExecutionSpan? selected = m_Binding.SelectedSpan;
            if (selected.HasValue || structureReset)
                ShowSelection(selected ?? m_SeekSelection);
            m_Version = m_Session.CaptureVersion;
            m_ProjectionVersion = m_Session.ProjectionVersion;
            m_TargetRevision = m_Session.TargetRevision;
            m_LastCaptureId = m_Session.CaptureId;
            m_LastState = m_Session.AttachmentState;
            m_Binding.SetCursor(m_PreviewTime());
            m_Surface.MarkDirtyRepaint();
        }

        void EnsureSurface()
        {
            if (m_Binding != null)
                return;
            m_Session.GetExecutionAuthoringCapacity(out int timelines, out int tracks);
            m_Binding = new ExecutionBinding(m_Session.ExecutionSpanCapacity, timelines, tracks,
                m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Logic),
                m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Presentation),
                () => m_Session.GetInvalidHistoryStart(RuntimeTraceDomain.Logic) is { } start
                    ? (float)(start - m_Binding.GetTimeOrigin(RuntimeTraceDomain.Logic)) : (float?)null,
                m_Surface.MarkDirtyRepaint, m_OpenSource, ShowSelection, Seek);
            m_Slate = ScriptableObject.CreateInstance<CutsceneEditorSurface>();
            m_Slate.InitializeEmbedded(m_Binding, m_Surface.MarkDirtyRepaint);
            m_Slate.ConfigureEmbeddedRuntimeTime(() => m_Session.IsFollowingExecution
                ? m_PreviewTime() : (float?)null);
            m_Slate.ConfigureEmbeddedHistoryTime(() => !m_Session.IsFollowingExecution
                ? m_Binding.CurrentTime : (float?)null);
            m_Slate.ConfigureEmbeddedInvalidHistoryTime(() =>
                m_Session.GetInvalidHistoryStart(RuntimeTraceDomain.Logic) is { } start
                    ? (float)(start - m_Binding.GetTimeOrigin(RuntimeTraceDomain.Logic)) : null);
        }

        void ShowSelection(RuntimeExecutionSpan? selection)
        {
            m_SeekSelection = selection;
        }

        void Seek(float timeSeconds)
        {
            m_SeekPreview(timeSeconds);
            m_Binding.SetCursor(m_PreviewTime());
        }

        void Draw()
        {
            using var profile = s_Draw.Auto();
            if (m_Binding != null && m_CanSeekPreview())
            {
                float viewStart = m_Binding.ViewTimeMin;
                float viewEnd = m_Binding.ViewTimeMax;
                m_Slate.DrawEmbeddedGUI(Mathf.Max(1, m_Surface.contentRect.width),
                    Mathf.Max(1, m_Surface.contentRect.height), m_BeginWindows, m_EndWindows);
                float padding = m_Slate.TimelineReadPaddingSeconds;
                bool userNavigation = viewStart != m_Binding.ViewTimeMin || viewEnd != m_Binding.ViewTimeMax;
                double origin = m_Binding.GetTimeOrigin(RuntimeTraceDomain.Logic);
                m_Session.SetExecutionWindow(RuntimeTraceDomain.Logic, origin + m_Binding.ViewTimeMin,
                    origin + m_Binding.ViewTimeMax, padding,
                    userNavigation);
            }
        }

        void ClearProjection()
        {
            m_Binding?.Clear();
            ClearSelection();
            m_Version = -1;
            m_TargetRevision = -1;
            m_Surface.MarkDirtyRepaint();
        }

        void ReleaseSurface()
        {
            ClearProjection();
            if (m_Slate != null)
            {
                m_Slate.ClearEmbedded();
                UnityEngine.Object.DestroyImmediate(m_Slate);
            }
            m_Slate = null;
            m_Binding = null;
        }

        void ClearSelection()
        {
            m_SeekSelection = null;
        }

        public void Dispose()
        {
            m_Session.Changed -= RefreshProjection;
            m_Session.HistoryCursorChanged -= RefreshLiveCursor;
            EditorApplication.update -= RefreshLiveCursor;
            ReleaseSurface();
        }

        sealed class ExecutionBinding : IEmbeddedTimelineBinding
        {
            double m_LogicTimeOrigin;
            double m_PresentationTimeOrigin;
            readonly Action m_Repaint;
            readonly Action<RuntimeExecutionSpan> m_Open;
            readonly Action<float> m_Seek;
            readonly Func<float?> m_InvalidHistoryStart;
            readonly Action<RuntimeExecutionSpan?> m_Select;
            readonly List<IEmbeddedTimelineGroupBinding> m_Groups;
            readonly Dictionary<string, Group> m_GroupMap;
            Group[] m_GroupPool = Array.Empty<Group>();
            Clip[] m_Clips;
            Track[] m_Tracks = Array.Empty<Track>();
            readonly Dictionary<(string Timeline, string Track), Track> m_TrackMap;
            RuntimeExecutionTimeline m_Timeline;
            long m_ProjectionRevision = -1;
            long m_HistoryBranchRevision = -1;
            int m_Processed;
            float m_Cursor;
            float m_Extent;

            internal ExecutionBinding(int capacity, int timelineCapacity, int trackCapacity,
                double logicTimeOrigin, double presentationTimeOrigin, Func<float?> invalidHistoryStart, Action repaint,
                Action<RuntimeExecutionSpan> open, Action<RuntimeExecutionSpan?> select, Action<float> seek)
            {
                m_LogicTimeOrigin = logicTimeOrigin;
                m_PresentationTimeOrigin = presentationTimeOrigin;
                m_Repaint = repaint;
                m_Open = open;
                m_Select = select;
                m_Seek = seek;
                m_InvalidHistoryStart = invalidHistoryStart;
                m_Groups = new List<IEmbeddedTimelineGroupBinding>(timelineCapacity);
                m_GroupMap = new Dictionary<string, Group>(timelineCapacity);
                m_Clips = new Clip[capacity];
                m_TrackMap = new Dictionary<(string Timeline, string Track), Track>(trackCapacity);
                for (int i = 0; i < capacity; i++)
                {
                    string id = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    m_Clips[i] = new Clip(this, i, id);
                }
                ReserveAuthoringCapacity(timelineCapacity, trackCapacity);
            }

            internal Guid CaptureId => m_Timeline?.CaptureId ?? Guid.Empty;
            internal double GetTimeOrigin(RuntimeTraceDomain domain) => domain == RuntimeTraceDomain.Presentation
                ? m_PresentationTimeOrigin : m_LogicTimeOrigin;
            internal RuntimeExecutionTimeline Timeline => m_Timeline;
            internal RuntimeExecutionSpan? SelectedSpan => Selected is Clip clip ? clip.Span : null;
            internal string SelectedStatus => Selected is Clip clip ? clip.Status : string.Empty;
            internal float LatestTimeSeconds { get; private set; }
            internal void ReserveAuthoringCapacity(int timelineCapacity, int trackCapacity)
            {
                if (trackCapacity > m_Tracks.Length)
                {
                    int first = m_Tracks.Length;
                    Array.Resize(ref m_Tracks, trackCapacity);
                    m_TrackMap.EnsureCapacity(trackCapacity);
                    for (int i = first; i < trackCapacity; i++)
                        m_Tracks[i] = new Track(this, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    for (int i = 0; i < m_GroupPool.Length; i++)
                        m_GroupPool[i].Items.Capacity = trackCapacity;
                }
                if (timelineCapacity > m_GroupPool.Length)
                {
                    int first = m_GroupPool.Length;
                    Array.Resize(ref m_GroupPool, timelineCapacity);
                    m_GroupMap.EnsureCapacity(timelineCapacity);
                    m_Groups.Capacity = timelineCapacity;
                    for (int i = first; i < timelineCapacity; i++)
                        m_GroupPool[i] = new Group(trackCapacity);
                }
            }

            internal void Clear()
            {
                ClearRows();
                m_GroupMap.Clear();
                m_Timeline = null;
                m_Cursor = m_Extent = LatestTimeSeconds = 0;
                ViewTimeMin = ViewTimeMax = 0;
            }

            internal void Reset(double logicTimeOrigin, double presentationTimeOrigin)
            {
                for (int i = 0; i < m_Tracks.Length; i++)
                {
                    m_Tracks[i].ShowCurves = false;
                    m_Tracks[i].CustomHeight = 0;
                }
                for (int i = 0; i < m_GroupPool.Length; i++)
                    m_GroupPool[i].IsCollapsed = false;
                m_GroupMap.Clear();
                m_LogicTimeOrigin = logicTimeOrigin;
                m_PresentationTimeOrigin = presentationTimeOrigin;
                m_Cursor = m_Extent = LatestTimeSeconds = 0;
                ViewTimeMin = ViewTimeMax = 0;
            }

            void ClearRows()
            {
                for (int i = 0; i < m_Processed; i++)
                    m_Clips[i].OwnerTrack = null;
                m_Processed = 0;
                m_TrackMap.Clear();
                for (int i = 0; i < m_Groups.Count; i++)
                    ((Group)m_Groups[i]).Items.Clear();
                m_Groups.Clear();
                Selected = null;
            }

            internal bool Apply(RuntimeExecutionTimeline timeline, bool live, ulong? selectedStartSequence)
            {
                float previousEnd = Length;
                RuntimeDebugSession session = RuntimeDebugSession.Shared;
                bool historyChanged = m_HistoryBranchRevision != session.HistoryBranchRevision;
                bool reset = !ReferenceEquals(m_Timeline, timeline) ||
                    m_ProjectionRevision != timeline.ProjectionRevision || historyChanged;
                m_ProjectionRevision = timeline.ProjectionRevision;
                m_HistoryBranchRevision = session.HistoryBranchRevision;
                m_Timeline = timeline;
                if (timeline.Spans.Count > m_Clips.Length)
                {
                    int first = m_Clips.Length;
                    Array.Resize(ref m_Clips, Math.Max(first * 2, timeline.Spans.Count));
                    for (int i = first; i < m_Clips.Length; i++)
                        m_Clips[i] = new Clip(this, i, i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                if (reset)
                    ClearRows();
                for (int i = m_Processed; i < timeline.Spans.Count; i++)
                {
                    ref readonly RuntimeCaptureSpanData span = ref timeline.GetSpanData(i);
                    if (!session.TryGetHistoryTimeRange(span.Start.ExecutionBranchId, span.Start.Domain,
                        span.Start.TimeSeconds, timeline.GetObservedEndTime(i), out _, out _))
                        continue;
                    ref readonly RuntimeSourceElementKey sourceKey = ref timeline.GetSpanSource(i);
                    session.TryResolveHistoricalSource(span.Start.ContentRevision, span.Start.Source, out _, out DebugSourceMapEntry source);
                    var key = (sourceKey.TimelineAuthoringId, sourceKey.TrackAuthoringId);
                    if (!m_TrackMap.TryGetValue(key, out Track track))
                    {
                        session.TryResolveHistoricalSource(span.Start.ContentRevision, source.Parent, out _, out DebugSourceMapEntry authorTrack);
                        session.TryResolveHistoricalSource(span.Start.ContentRevision, authorTrack.Parent, out _, out DebugSourceMapEntry authorTimeline);
                        string timelineId = sourceKey.TimelineAuthoringId;
                        if (!m_GroupMap.TryGetValue(timelineId, out Group group))
                        {
                            group = m_GroupPool[m_GroupMap.Count];
                            group.Bind(timelineId, authorTimeline.DisplayName);
                            m_GroupMap.Add(timelineId, group);
                        }
                        if (group.Items.Count == 0)
                            m_Groups.Add(group);
                        track = m_Tracks[m_TrackMap.Count];
                        track.Reset(authorTrack.DisplayName);
                        m_TrackMap.Add(key, track);
                        group.Items.Add(track);
                    }
                    Clip clip = m_Clips[i];
                    clip.OwnerTrack = track;
                    clip.DisplayName = source.DisplayName;
                    track.Append(clip);
                    if (reset && span.Start.Sequence == selectedStartSequence)
                        Selected = clip;
                }
                m_Processed = timeline.Spans.Count;
                LatestTimeSeconds = (float)Math.Max(
                    session.GetLatestHistoryTime(timeline, RuntimeTraceDomain.Logic) - m_LogicTimeOrigin,
                    session.GetLatestHistoryTime(timeline, RuntimeTraceDomain.Presentation) - m_PresentationTimeOrigin);
                m_Extent = historyChanged ? LatestTimeSeconds : Mathf.Max(m_Extent, LatestTimeSeconds);
                if (historyChanged)
                    ViewTimeMax = Mathf.Min(ViewTimeMax, Length);
                m_Cursor = LatestTimeSeconds;
                RefreshTimeRange(live && ViewTimeMax == previousEnd);
                return reset;
            }

            internal void RefreshTimeRange(bool followEnd)
            {
                float end = Length;
                if (followEnd || ViewTimeMax == 0)
                    ViewTimeMax = Mathf.Max(ViewTimeMax, end);
                if (ViewTimeMin >= ViewTimeMax)
                    ViewTimeMin = Mathf.Max(0, ViewTimeMax - SnapInterval);
            }

            internal void SetCursor(float value)
            {
                if (m_Cursor == value)
                    return;
                m_Cursor = value;
                m_Repaint();
            }
            public string DisplayName => "轨道执行记录";
            public int FrameRate => Prefs.frameRate;
            public float SnapTime(float time) => time;
            public string SnapLabel => "秒";
            public void ShowSnapSettings(Rect rect) => EditorGUI.LabelField(rect, SnapLabel);
            public float Length => Mathf.Max(SnapInterval, Mathf.Min(m_Extent, m_InvalidHistoryStart() ?? m_Extent));
            public float CurrentTime { get => m_Cursor; set => m_Seek(value); }
            public bool DisplayFrames { get; set; }
            public float SnapInterval => 1f / FrameRate;
            public float StepInterval => 1f / FrameRate;
            public float ViewTimeMin { get; set; }
            public float ViewTimeMax { get; set; }
            public bool IsReadOnly => true;
            public bool TimeRangeOnly => false;
            public bool ResourceEditing => false;
            public IReadOnlyList<IEmbeddedTimelineGroupBinding> Groups => m_Groups;
            public IReadOnlyList<IEmbeddedTimelineSectionBinding> Sections => Array.Empty<IEmbeddedTimelineSectionBinding>();
            public IEmbeddedTimelineElementBinding Selected { get; private set; }
            public void Select(IEmbeddedTimelineElementBinding element)
            {
                Selected = element;
                m_Select(SelectedSpan);
                m_Repaint();
            }
            public bool TryGetTrack(string id, out IEmbeddedTimelineTrackBinding track)
            {
                for (int i = 0; i < m_TrackMap.Count; i++)
                    if (m_Tracks[i].AuthoringId == id) { track = m_Tracks[i]; return true; }
                track = null;
                return false;
            }
            public bool TryGetClip(string id, out IEmbeddedTimelineClipBinding clip)
            {
                for (int i = 0; i < m_Processed; i++)
                    if (m_Clips[i].AuthoringId == id && m_Clips[i].OwnerTrack != null) { clip = m_Clips[i]; return true; }
                clip = null;
                return false;
            }
            public Action CreateSourceNavigation(IEmbeddedTimelineClipBinding clip)
            {
                RuntimeExecutionSpan span = ((Clip)clip).Span;
                return () => m_Open(span);
            }
            public void RequestRepaint() => m_Repaint();
            public bool CanPasteClip => false;
            static InvalidOperationException ReadOnly() => new InvalidOperationException("执行记录只读，不能修改已发生的调用。");
            public void AddTrack() => throw ReadOnly();
            public void AddClip(IEmbeddedTimelineTrackBinding track, double time) => throw ReadOnly();
            public void AddMarker(IEmbeddedTimelineTrackBinding track, double time) => throw ReadOnly();
            public void DeleteMarker(IEmbeddedTimelineMarkerBinding marker) => throw ReadOnly();
            public void MoveMarker(IEmbeddedTimelineMarkerBinding marker, double time) => throw ReadOnly();
            public void OpenMarker(IEmbeddedTimelineMarkerBinding marker) => throw ReadOnly();
            public void SetTrackActive(IEmbeddedTimelineTrackBinding track, bool active) => throw ReadOnly();
            public void DeleteTrack(IEmbeddedTimelineTrackBinding track) => throw ReadOnly();
            public void DeleteClip(IEmbeddedTimelineClipBinding clip) => throw ReadOnly();
            public void DeleteClips(IReadOnlyList<IEmbeddedTimelineClipBinding> clips) => throw ReadOnly();
            public void SplitClip(IEmbeddedTimelineClipBinding clip, double time) => throw ReadOnly();
            public void MoveTrack(IEmbeddedTimelineTrackBinding track, int index) => throw ReadOnly();
            public void ConfigureSection(IEmbeddedTimelineSectionBinding section, string name, double time) => throw ReadOnly();
            public void DeleteSection(IEmbeddedTimelineSectionBinding section) => throw ReadOnly();
            public void AddSection(double time) => throw ReadOnly();
            public void CopyClip(IEmbeddedTimelineClipBinding clip) => throw ReadOnly();
            public void PasteClip(IEmbeddedTimelineTrackBinding track, double time) => throw ReadOnly();
            public void BeginEdit(string undoName, EmbeddedTimelineEditKind kind = EmbeddedTimelineEditKind.Content) => throw ReadOnly();
            public void CommitEdit() => throw ReadOnly();
            public void CancelEdit() => throw ReadOnly();

            sealed class Group : IEmbeddedTimelineGroupBinding
            {
                internal readonly List<IEmbeddedTimelineTrackBinding> Items;
                internal Group(int capacity)
                {
                    Items = new List<IEmbeddedTimelineTrackBinding>(capacity);
                }
                internal void Bind(string id, string name)
                {
                    AuthoringId = id;
                    DisplayName = name;
                }
                public string AuthoringId { get; private set; }
                public string DisplayName { get; private set; }
                public bool IsLocked { get => false; set => throw ReadOnly(); }
                public bool IsActive { get => true; set => throw ReadOnly(); }
                public bool IsCollapsed { get; set; }
                public IReadOnlyList<IEmbeddedTimelineTrackBinding> Tracks => Items;
            }

            sealed class Track : IEmbeddedTimelineTrackBinding, IReadOnlyList<IEmbeddedTimelineClipBinding>
            {
                readonly ExecutionBinding m_Owner;
                Clip m_First;
                Clip m_Last;
                Clip m_Cached;
                int m_CachedIndex;
                internal Track(ExecutionBinding owner, string id) { m_Owner = owner; AuthoringId = id; }
                internal void Reset(string displayName)
                {
                    m_First = m_Last = m_Cached = null;
                    m_CachedIndex = 0;
                    Count = 0;
                    DisplayName = displayName;
                }
                internal void Append(Clip clip)
                {
                    clip.Previous = m_Last;
                    clip.Next = null;
                    if (m_Last != null) m_Last.Next = clip;
                    else m_First = m_Cached = clip;
                    m_Last = clip;
                    Count++;
                }
                public int Count { get; private set; }
                public IEmbeddedTimelineClipBinding this[int index]
                {
                    get
                    {
                        if (index == Count - 1) { m_Cached = m_Last; m_CachedIndex = index; }
                        while (m_CachedIndex < index) { m_Cached = m_Cached.Next; m_CachedIndex++; }
                        while (m_CachedIndex > index) { m_Cached = m_Cached.Previous; m_CachedIndex--; }
                        return m_Cached;
                    }
                }
                public IEnumerator<IEmbeddedTimelineClipBinding> GetEnumerator()
                {
                    for (Clip clip = m_First; clip != null; clip = clip.Next) yield return clip;
                }
                IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
                public string AuthoringId { get; }
                public string DisplayName { get; private set; }
                public bool IsLocked { get => false; set => throw ReadOnly(); }
                public bool IsActive { get => true; set => throw ReadOnly(); }
                public bool ShowCurves { get; set; }
                public float CustomHeight { get; set; }
                public Color Color => new Color(0.3f, 0.65f, 0.85f);
                public float StartTime => m_First.StartTime;
                public float EndTime => m_Owner.Length;
                public float DefaultHeight => 24;
                public float FinalHeight => Mathf.Max(DefaultHeight, CustomHeight);
                public float GetFinalHeight(string inspectedParameterId) => FinalHeight;
                public IReadOnlyList<IEmbeddedTimelineClipBinding> Clips => this;
                public IEmbeddedTimelineClipBinding SelectedClip => m_Owner.Selected is Clip clip && clip.OwnerTrack == this ? clip : null;
            }

            sealed class Clip : IEmbeddedTimelineClipBinding
            {
                readonly ExecutionBinding m_Owner;
                readonly int m_Index;
                internal Track OwnerTrack;
                internal Clip Previous;
                internal Clip Next;
                internal Clip(ExecutionBinding owner, int index, string id) { m_Owner = owner; m_Index = index; AuthoringId = id; }
                internal RuntimeExecutionSpan Span => m_Owner.m_Timeline.Spans[m_Index];
                public string AuthoringId { get; }
                public string DisplayName { get; internal set; }
                public bool IsLocked { get => false; set => throw ReadOnly(); }
                public IEmbeddedTimelineTrackBinding Track => OwnerTrack;
                public string Info => DisplayName;
                internal string Status
                {
                    get
                    {
                        ref readonly RuntimeCaptureSpanData span = ref m_Owner.m_Timeline.GetSpanData(m_Index);
                        if (span.Start.Kind == RuntimeTraceEventKind.ClipActive)
                            return "活跃采样";
                        if (span.State == RuntimeCaptureSpanState.MissingStart)
                            return "缺少进入记录";
                        if (span.State == RuntimeCaptureSpanState.Open)
                            return "执行中";
                        var status = span.End.GetStatus();
                        return status.IsEmpty ? RuntimeTraceDisplay.Detail(span.End.Payload) : status.ToString();
                    }
                }
                public bool IsActive => true;
                public bool IsTimeQuantized => false;
                public bool IsValid => true;
                public bool IsCollapsed { get; set; }
                public float StartTime
                {
                    get
                    {
                        GetTimeBounds(out double start, out _, out double origin);
                        return (float)(start - origin);
                    }
                    set => throw ReadOnly();
                }
                public float EndTime
                {
                    get
                    {
                        GetTimeBounds(out _, out double end, out double origin);
                        return (float)(end - origin);
                    }
                    set => throw ReadOnly();
                }
                public float Length
                {
                    get
                    {
                        GetTimeBounds(out double start, out double end, out _);
                        return (float)(end - start);
                    }
                }
                void GetTimeBounds(out double start, out double end, out double origin)
                {
                    ref readonly RuntimeCaptureSpanData span = ref m_Owner.m_Timeline.GetSpanData(m_Index);
                    RuntimeDebugSession.Shared.TryGetHistoryTimeRange(span.Start.ExecutionBranchId, span.Start.Domain,
                        span.Start.TimeSeconds, m_Owner.m_Timeline.GetObservedEndTime(m_Index), out start, out end);
                    origin = m_Owner.GetTimeOrigin(span.Start.Domain);
                }
                public float BlendIn { get => 0; set => throw ReadOnly(); }
                public float BlendOut { get => 0; set => throw ReadOnly(); }
                public bool CanScale => false;
                public bool CanClipIn => false;
                public double ClipInTime { get => 0; set => throw ReadOnly(); }
                public bool CanBlendIn => false;
                public bool CanBlendOut => false;
                public ulong CurveRevision => 0;
                public IReadOnlyList<IEmbeddedTimelineParameterBinding> Parameters => Array.Empty<IEmbeddedTimelineParameterBinding>();
                public IReadOnlyList<IEmbeddedTimelineCurveBinding> Curves => Array.Empty<IEmbeddedTimelineCurveBinding>();
                public bool CanCrossBlend(IEmbeddedTimelineClipBinding other) => false;
                public void AddIdentityKey(float time) => throw ReadOnly();
                public void Split(float time) => throw ReadOnly();
                public void StretchFit() => throw ReadOnly();
                public void CleanKeysOffRange() => throw ReadOnly();
                public void ResetAnimation() => throw ReadOnly();
            }
        }
    }
}
