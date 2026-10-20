using System;
using System.Collections.Generic;
using System.Diagnostics;
using BTSMTL.Diagnostics;
using BTSMTL.Diagnostics.Editor;
using BTSMTL.Orchestration;
using UnityEditor;
using UnityEngine;

namespace BTSMTL.Timeline.Editor
{
    public sealed class BtsmtlExecutionTimeBarDataSource : BtsmtlTimeBarDataSource
    {
        sealed class SpanContext
        {
            internal RuntimeExecutionSpan Span;
            internal double ObservedStart;
            internal double ObservedEnd;
            internal RuntimeExecutionPlaybackCursors Cursors;
            internal TimelineAsset Authoring;
            internal string SourceError;
            internal bool Provider;
        }

        readonly struct StateKey : IEquatable<StateKey>
        {
            readonly Guid m_Runtime;
            readonly string m_Graph;
            readonly string m_State;
            readonly ulong m_Generation;
            readonly ulong m_Action;
            readonly ulong m_Execution;
            readonly Guid m_Branch;
            readonly ulong m_Epoch;

            internal StateKey(SpanContext state)
            {
                RuntimeExecutionSpan span = state.Span;
                m_Runtime = span.Instance.GraphRuntimeId;
                m_Graph = span.Source.GraphAuthoringId;
                m_State = span.Kind == RuntimeExecutionSpanKind.Transition
                    ? span.Start.Payload.OwnerId : span.Source.ElementAuthoringId;
                m_Generation = span.ActivationGeneration;
                m_Action = span.ActionInstanceId;
                m_Execution = span.SkillExecutionGeneration;
                m_Branch = span.ExecutionBranchId;
                m_Epoch = span.RuntimeEpoch;
            }

            internal StateKey(in RuntimeExecutionSpan playback)
            {
                RuntimeTimelinePlaybackProvenance provenance = playback.Start.Payload.TimelinePlayback;
                m_Runtime = provenance.StateMachineGraphRuntimeId;
                m_Graph = provenance.StateMachineGraphAuthoringId;
                m_State = provenance.StateId;
                m_Generation = provenance.StateActivationGeneration;
                m_Action = playback.ActionInstanceId;
                m_Execution = playback.SkillExecutionGeneration;
                m_Branch = playback.ExecutionBranchId;
                m_Epoch = playback.RuntimeEpoch;
            }
            public bool Equals(StateKey other) => m_Runtime == other.m_Runtime && m_Graph == other.m_Graph &&
                m_State == other.m_State && m_Generation == other.m_Generation && m_Action == other.m_Action &&
                m_Execution == other.m_Execution && m_Branch == other.m_Branch && m_Epoch == other.m_Epoch;
            public override bool Equals(object obj) => obj is StateKey other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(m_Runtime, m_Graph, m_State, m_Generation,
                m_Action, m_Execution, m_Branch, m_Epoch);
        }

        sealed class StateGroup
        {
            internal SpanContext State;
            internal readonly List<PlaybackGroup> Playbacks = new();
            internal readonly List<SpanContext> Transitions = new();
        }

        sealed class PlaybackGroup
        {
            internal RuntimeExecutionPlaybackKey Key;
            internal SpanContext Source;
            internal string UnassignedReason;
            internal readonly List<SpanContext> Spans = new();
            internal double Start = double.PositiveInfinity;
            internal double End;
        }

        readonly List<BtsmtlTimeBarRow> m_Rows = new();
        readonly List<BtsmtlTimeBarItem> m_Items = new();
        readonly Dictionary<StateKey, StateGroup> m_States = new();
        readonly HashSet<StateKey> m_AmbiguousStates = new();
        readonly List<StateGroup> m_StateOrder = new();
        readonly Dictionary<RuntimeExecutionPlaybackKey, PlaybackGroup> m_Playbacks = new();
        readonly List<SpanContext> m_Transitions = new();
        readonly RuntimeDebugSession m_Session;
        readonly Action<RuntimeExecutionSpan> m_Open;
        readonly Action<float> m_Seek;
        readonly Func<float> m_Time;
        readonly Func<bool> m_CanSeek;
        IBtsmtlExecutionTimelineAuthoringHost m_Authoring;
        ITimelineTimeRangePreview m_Edit;
        TimelineClipTimeRange[] m_Ranges;
        int m_EditSlot;
        BtsmtlTimeBarItem m_Edited;
        double m_EditStart;
        double m_EditEnd;
        BtsmtlTimeBarEditKind m_EditKind;
        TimelineClipTimeRange m_EditOriginalRange;
        bool m_EditChanged;
        long m_EditResultRevision;
        bool m_AwaitingResult;
        double m_LogicOrigin;
        double m_PresentationOrigin;
        double m_End;
        double m_DisplayedTime;
        long m_Revision;

        public BtsmtlExecutionTimeBarDataSource(RuntimeDebugSession session, Action<RuntimeExecutionSpan> open,
            Action<float> seek, Func<float> time, Func<bool> canSeek)
        {
            m_Session = session;
            m_Open = open;
            m_Seek = seek;
            m_Time = time;
            m_CanSeek = canSeek;
        }

        public void ConfigureAuthoring(IBtsmtlExecutionTimelineAuthoringHost authoring) => m_Authoring = authoring;
        public override IReadOnlyList<BtsmtlTimeBarRow> Rows => m_Rows;
        public override IReadOnlyList<BtsmtlTimeBarItem> Items => m_Items;
        public override double StartTime => 0;
        public override double EndTime => Math.Max(.02, m_End);
        public override double CurrentTime
        {
            get
            {
                if (m_CanSeek()) m_DisplayedTime = m_Time();
                return m_DisplayedTime;
            }
            set { if (m_CanSeek()) m_Seek((float)value); }
        }
        public override long Revision => m_Revision;
        public override string Status => EditError ?? (m_AwaitingResult ? "正在重算；保留上一完整结果，后续区间待更新。" : null);
        public Guid CaptureId { get; private set; }
        public bool IsAwaitingResult => m_AwaitingResult;
        public string EditError { get; private set; }

        public bool RestoreFailedResult()
        {
            if (!m_AwaitingResult || string.IsNullOrEmpty(m_Authoring.TimeRangeResultError))
                return false;
            EditError = $"重算失败，显示上一完整结果；作者时间修改已保留。{m_Authoring.TimeRangeResultError}";
            RestoreCompleteResult();
            return true;
        }

        public bool Apply(RuntimeExecutionTimeline timeline)
        {
            if (m_Edit != null)
                return false;
            if (m_AwaitingResult)
            {
                if (RestoreFailedResult())
                    return false;
                if (m_Authoring.IsTimeRangeResultPending ||
                    m_EditResultRevision == m_Authoring.AdoptedTimeRangeResultRevision)
                    return false;
                m_AwaitingResult = false;
                m_Edited = null;
            }
            CaptureId = timeline.CaptureId;
            m_LogicOrigin = m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Logic);
            m_PresentationOrigin = m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Presentation);
            m_Rows.Clear();
            m_Items.Clear();
            m_States.Clear();
            m_AmbiguousStates.Clear();
            m_StateOrder.Clear();
            m_Playbacks.Clear();
            m_Transitions.Clear();
            double logicEnd = m_Session.GetLatestHistoryTime(timeline, RuntimeTraceDomain.Logic);
            double presentationEnd = m_Session.GetLatestHistoryTime(timeline, RuntimeTraceDomain.Presentation);
            if (m_Session.GetInvalidHistoryStart(RuntimeTraceDomain.Logic) is { } invalidLogic)
                logicEnd = Math.Min(logicEnd, invalidLogic);
            if (m_Session.GetInvalidHistoryStart(RuntimeTraceDomain.Presentation) is { } invalidPresentation)
                presentationEnd = Math.Min(presentationEnd, invalidPresentation);
            m_End = Math.Max(logicEnd - m_LogicOrigin, presentationEnd - m_PresentationOrigin);
            for (int i = 0; i < timeline.Spans.Count; i++)
            {
                RuntimeExecutionSpan span = timeline.Spans[i];
                double observedEnd = timeline.GetObservedEndTime(i);
                if (!m_Session.TryGetHistoryTimeRange(span.ExecutionBranchId, span.Domain,
                    span.Start.TimeSeconds, observedEnd, out double first, out double last))
                    continue;
                var context = new SpanContext { Span = span, ObservedStart = first, ObservedEnd = last };
                if (span.Kind == RuntimeExecutionSpanKind.State)
                {
                    var group = new StateGroup { State = context };
                    var key = new StateKey(context);
                    if (!m_States.TryAdd(key, group))
                        m_AmbiguousStates.Add(key);
                    m_StateOrder.Add(group);
                }
                else if (span.Kind == RuntimeExecutionSpanKind.Transition)
                    m_Transitions.Add(context);
                else if (span.Instance.TimelinePlaybackId != 0)
                    AddPlayback(context, timeline);
            }
            foreach (PlaybackGroup playback in m_Playbacks.Values)
            {
                RuntimeExecutionSpan span = playback.Source.Span;
                if (!span.Start.Payload.TimelinePlayback.HasStateActivation)
                    playback.UnassignedReason = "播放来源未记录有效状态身份。";
                else if (m_AmbiguousStates.Contains(new StateKey(in span)))
                    playback.UnassignedReason = "记录中的状态来源身份不唯一，无法确定所属状态实例。";
                else if (m_States.TryGetValue(new StateKey(in span), out StateGroup state))
                    state.Playbacks.Add(playback);
                else
                    playback.UnassignedReason = "该状态实例的进入记录不在当前记录范围内。";
            }
            foreach (SpanContext transition in m_Transitions)
                if (!m_AmbiguousStates.Contains(new StateKey(transition)) &&
                    m_States.TryGetValue(new StateKey(transition), out StateGroup state))
                    state.Transitions.Add(transition);
            m_StateOrder.Sort((left, right) => left.State.Span.Start.TimeSeconds.CompareTo(right.State.Span.Start.TimeSeconds));
            foreach (StateGroup group in m_StateOrder)
                DrawState(group);
            int unassigned = -1;
            foreach (PlaybackGroup playback in m_Playbacks.Values)
                if (playback.UnassignedReason != null)
                {
                    if (unassigned < 0)
                        unassigned = AddRow("unassigned", -1, BtsmtlTimeBarItemKind.State, "未归属播放（来源身份缺失）");
                    DrawPlayback(playback, unassigned);
                }
            foreach (SpanContext transition in m_Transitions)
                if (m_AmbiguousStates.Contains(new StateKey(transition)) || !m_States.ContainsKey(new StateKey(transition)))
                {
                    if (unassigned < 0)
                        unassigned = AddRow("unassigned", -1, BtsmtlTimeBarItemKind.State, "未归属记录（来源身份缺失）");
                    AddMarker(transition, unassigned, BtsmtlTimeBarItemKind.Transition,
                        m_AmbiguousStates.Contains(new StateKey(transition)) ? "转移 · 源状态身份不唯一" : "转移 · 缺少源状态记录");
                }
            HighlightProviders();
            m_Revision++;
            return true;
        }

        void AddPlayback(SpanContext context, RuntimeExecutionTimeline timeline)
        {
            RuntimeExecutionSpan span = context.Span;
            RuntimeExecutionPlaybackKey cursorKey = span.Playback;
            timeline.Cursors.TryGet(in cursorKey, out context.Cursors);
            var key = new RuntimeExecutionPlaybackKey(span.Instance, span.ExecutionBranchId,
                span.RuntimeEpoch, span.ContentRevision, RuntimeTraceDomain.Logic);
            if (!m_Playbacks.TryGetValue(key, out PlaybackGroup group))
            {
                group = new PlaybackGroup { Key = key, Source = context };
                m_Playbacks.Add(key, group);
            }
            double origin = Origin(span.Domain);
            group.Start = Math.Min(group.Start, context.ObservedStart - origin);
            group.End = Math.Max(group.End, context.ObservedEnd - origin);
            if (span.Kind is RuntimeExecutionSpanKind.Clip or RuntimeExecutionSpanKind.TreeClip)
            {
                if (m_Authoring != null)
                {
                    m_Authoring.TryResolveTimeline(span.Source.TimelineAuthoringId, out context.Authoring, out context.SourceError);
                    RuntimeTimelinePlaybackProvenance owner = span.Start.Payload.TimelinePlayback;
                    RuntimeSourceElementKey source = span.Source;
                    context.Provider = m_Authoring.IsTransitionProvider(in owner, span.ContentRevision, in source);
                }
                group.Spans.Add(context);
            }
        }

        void DrawState(StateGroup group)
        {
            RuntimeExecutionSpan span = group.State.Span;
            string name = Name(span);
            int row = AddRow("state:" + span.StartSequence, -1, BtsmtlTimeBarItemKind.State,
                $"{name} · 第 {span.ActivationGeneration} 次 · 动作 {span.ActionInstanceId}");
            AddSpan(group.State, row, BtsmtlTimeBarItemKind.State, name);
            foreach (SpanContext transition in group.Transitions)
                AddMarker(transition, row, BtsmtlTimeBarItemKind.Transition, "转移");
            group.Playbacks.Sort((left, right) => left.Start.CompareTo(right.Start));
            foreach (PlaybackGroup playback in group.Playbacks)
                DrawPlayback(playback, row);
        }

        void DrawPlayback(PlaybackGroup playback, int parent)
        {
            RuntimeExecutionPlaybackCursors cursors;
            cursors = playback.Source.Cursors;
            if (cursors == null)
            {
                foreach (SpanContext span in playback.Spans)
                    if (span.Cursors != null && span.Span.Domain == RuntimeTraceDomain.Logic)
                    {
                        cursors = span.Cursors;
                        break;
                    }
            }
            if (cursors == null && playback.Spans.Count != 0)
                cursors = playback.Spans[0].Cursors;
            if (cursors == null || cursors.Samples.Count == 0)
            {
                DrawCycle(playback, parent, "缺少游标记录", playback.Start, playback.End, 0);
                return;
            }
            double origin = Origin(cursors.Domain);
            int cycle = cursors.Samples[0].Cycle;
            double start = playback.Start;
            int segment = 0;
            for (int i = 1; i < cursors.Samples.Count; i++)
            {
                RuntimeExecutionCursorSample sample = cursors.Samples[i];
                if (sample.Cycle == cycle)
                    continue;
                double boundary = sample.SessionTime - origin;
                DrawCycle(playback, parent, CycleLabel(cycle), start, boundary, segment++);
                start = boundary;
                cycle = sample.Cycle;
            }
            DrawCycle(playback, parent, CycleLabel(cycle), start, playback.End, segment);
        }

        static string CycleLabel(int cycle) => cycle == 0 ? string.Empty : $" · 第 {cycle + 1} 圈";

        void DrawCycle(PlaybackGroup playback, int parent, string suffix, double from, double to, int segment)
        {
            string prefix = $"{playback.Key.Branch:N}:{playback.Key.Playback}:{playback.Key.Action}:{segment}";
            int playbackRow = AddRow(prefix + ":play", parent, BtsmtlTimeBarItemKind.Playback,
                $"{Name(playback.Source.Span)} · 播放 {playback.Key.Playback}{suffix}");
            m_Items.Add(new BtsmtlTimeBarItem(prefix + ":play", playbackRow, BtsmtlTimeBarItemKind.Playback,
                from, to, "播放", playback.UnassignedReason, playback.Source));
            var tracks = new Dictionary<(string, RuntimeTraceDomain), int>();
            foreach (SpanContext context in playback.Spans)
            {
                RuntimeExecutionSpan span = context.Span;
                double origin = Origin(span.Domain);
                double start = Math.Max(from, context.ObservedStart - origin);
                double end = Math.Min(to, context.ObservedEnd - origin);
                if (end < start || end == start && (span.Start.TimeSeconds - origin < from ||
                    span.Start.TimeSeconds - origin >= to && to > from))
                    continue;
                var trackKey = (span.Source.TrackAuthoringId, span.Domain);
                if (!tracks.TryGetValue(trackKey, out int row))
                {
                    row = AddRow(prefix + ":track:" + tracks.Count, playbackRow, BtsmtlTimeBarItemKind.Clip,
                        TrackName(span) + (span.Domain == RuntimeTraceDomain.Logic ? " · 逻辑" : " · 表现"));
                    tracks.Add(trackKey, row);
                }
                BtsmtlTimeBarItemKind kind = context.Provider ? BtsmtlTimeBarItemKind.Provider :
                    span.Kind == RuntimeExecutionSpanKind.TreeClip ? BtsmtlTimeBarItemKind.TreeClip : BtsmtlTimeBarItemKind.Clip;
                string tooltip = $"{Name(span)}\n动作 {span.ActionInstanceId} · 激活 {span.ActivationGeneration}\n{span.ContentRevision}";
                if (playback.UnassignedReason != null) tooltip += "\n" + playback.UnassignedReason;
                if (context.SourceError != null) tooltip += "\n" + context.SourceError;
                m_Items.Add(new BtsmtlTimeBarItem(prefix + ":span:" + span.StartSequence + ":" + span.Domain,
                    row, kind, start, end, Name(span), tooltip, context));
            }
        }

        void AddSpan(SpanContext context, int row, BtsmtlTimeBarItemKind kind, string label)
        {
            RuntimeExecutionSpan span = context.Span;
            m_Items.Add(new BtsmtlTimeBarItem("span:" + span.StartSequence, row, kind,
                context.ObservedStart - Origin(span.Domain), context.ObservedEnd - Origin(span.Domain), label,
                span.IsOpen ? "状态尚未退出，延伸到该分支最新提交时间。" :
                    span.State == RuntimeExecutionSpanState.MissingStart ? "缺少进入记录。" : label, context));
        }

        void AddMarker(SpanContext context, int row, BtsmtlTimeBarItemKind kind, string label)
        {
            double time = context.Span.Start.TimeSeconds - Origin(context.Span.Domain);
            m_Items.Add(new BtsmtlTimeBarItem("marker:" + context.Span.StartSequence, row, kind, time, time,
                label, "实际记录；该时刻由转移条件决定。双击定位状态机边。", context));
        }

        void HighlightProviders()
        {
            foreach (BtsmtlTimeBarItem item in m_Items)
            {
                if (item.Kind != BtsmtlTimeBarItemKind.Provider)
                    continue;
                SpanContext provider = (SpanContext)item.Context;
                if (provider.Span.Domain != RuntimeTraceDomain.Logic)
                    continue;
                foreach (BtsmtlTimeBarItem marker in m_Items)
                {
                    if (marker.Kind != BtsmtlTimeBarItemKind.Transition ||
                        !ProvidingAt(provider, (SpanContext)marker.Context))
                        continue;
                    item.ProviderHighlighted = true;
                    item.Tooltip += "\n转移发生时正提供其所读状态（同一逻辑 Tick 的激活记录）。";
                    break;
                }
            }
        }

        int AddRow(string id, int parent, BtsmtlTimeBarItemKind kind, string label)
        {
            int index = m_Rows.Count;
            m_Rows.Add(new BtsmtlTimeBarRow(id, parent, kind, label));
            return index;
        }
        double Origin(RuntimeTraceDomain domain) => domain == RuntimeTraceDomain.Presentation ? m_PresentationOrigin : m_LogicOrigin;
        string Name(in RuntimeExecutionSpan span) => m_Session.TryResolveHistoricalSource(span.ContentRevision,
            span.SourceHandle, out _, out DebugSourceMapEntry entry) ? entry.DisplayName : "来源未映射";
        string TrackName(in RuntimeExecutionSpan span)
        {
            m_Session.TryResolveHistoricalSource(span.ContentRevision, span.SourceHandle, out _, out DebugSourceMapEntry entry);
            return m_Session.TryResolveHistoricalSource(span.ContentRevision, entry.Parent, out _, out DebugSourceMapEntry track)
                ? track.DisplayName : span.Source.TrackAuthoringId;
        }

        public override string GetReadOnlyReason(BtsmtlTimeBarItem item)
        {
            if (item.Kind is not BtsmtlTimeBarItemKind.Clip and not BtsmtlTimeBarItemKind.TreeClip and not BtsmtlTimeBarItemKind.Provider)
                return base.GetReadOnlyReason(item);
            SpanContext context = (SpanContext)item.Context;
            if (m_Authoring == null) return "当前宿主没有提供作者时间修改入口。";
            if (m_AwaitingResult) return "上一时间候选仍在等待完整结果。";
            if (context.Authoring == null) return context.SourceError ?? "作者 Timeline 来源无效。";
            if (context.Cursors == null) return "该次播放缺少逐 Tick 游标记录。";
            if (context.Span.State == RuntimeExecutionSpanState.MissingStart) return "缺少进入记录，不能换算完整作者范围。";
            return m_Authoring.GetTimeRangeUnavailableReason(context.Authoring, context.Span.ContentRevision);
        }

        public override bool TryBeginEdit(BtsmtlTimeBarItem item, BtsmtlTimeBarEditKind kind, out string reason)
        {
            reason = GetReadOnlyReason(item);
            if (reason != null) return false;
            SpanContext context = (SpanContext)item.Context;
            if (!m_Authoring.TryOpenTimeRangePreview(context.Authoring, context.Span.ContentRevision, out ITimelineTimeRangePreview preview, out reason))
                return false;
            if (preview.IsEditing || !preview.MatchesSource)
            {
                reason = preview.IsEditing ? "已有时间手势进行中。" : "记录版本与当前已准备作者内容不同。";
                return false;
            }
            m_Ranges = new TimelineClipTimeRange[preview.ClipCount];
            m_EditSlot = -1;
            for (int i = 0; i < m_Ranges.Length; i++)
                foreach (Track track in context.Authoring.Data.Tracks)
                    foreach (Clip clip in track.Clips)
                        if (clip.AuthoringId == preview.ClipIdentityAt(i))
                        {
                            m_Ranges[i] = clip.CaptureTimeRange();
                            if (track.AuthoringId == context.Span.Source.TrackAuthoringId &&
                                clip.AuthoringId == context.Span.Source.ClipAuthoringId)
                                m_EditSlot = i;
                        }
            if (m_EditSlot < 0)
            {
                reason = "当前作者 Timeline 已不含记录指向的 Track／Clip。";
                return false;
            }
            m_Edit = preview;
            m_Edited = item;
            m_EditStart = item.Start;
            m_EditEnd = item.End;
            m_EditKind = kind;
            m_EditOriginalRange = m_Ranges[m_EditSlot];
            m_EditChanged = false;
            m_EditResultRevision = m_Authoring.AdoptedTimeRangeResultRevision;
            m_Edit.BeginEdit("调整执行时间线中的作者 Clip");
            int outside = m_Authoring.GetOutsideReferenceCount(context.Authoring,
                context.Span.Source.TrackAuthoringId, context.Span.Source.ClipAuthoringId);
            reason = outside > 0 ? $"修改作用于原作者资产；另有 {outside} 处范围外引用。" : "修改作用于原作者资产；同一 Clip 的其它出现已高亮。";
            foreach (BtsmtlTimeBarItem occurrence in m_Items)
                if (occurrence.Context is SpanContext candidate && SameSource(context.Span.Source, candidate.Span.Source))
                    occurrence.SourceHighlighted = true;
            EditError = null;
            return true;
        }

        public override bool TrySetRange(BtsmtlTimeBarItem item, double start, double end, out string message)
        {
            SpanContext context = (SpanContext)item.Context;
            double duration = context.Authoring.Data.Duration;
            double origin = Origin(context.Span.Domain);
            if (!context.Cursors.TryMap(start + origin, duration, out double localStart, out string startMessage) ||
                !context.Cursors.TryMap(end + origin, duration, out double localEnd, out string endMessage))
            {
                message = startMessage;
                if (message == null)
                    context.Cursors.TryMap(end + origin, duration, out _, out message);
                EditError = message;
                return false;
            }
            if (!context.Cursors.TryMap(m_EditStart + origin, duration, out double originalStart, out message) ||
                !context.Cursors.TryMap(m_EditEnd + origin, duration, out double originalEnd, out message))
            {
                EditError = message;
                return false;
            }
            TimelineClipTimeRange source = m_EditOriginalRange;
            OrchestrationTime candidateStart = m_EditKind == BtsmtlTimeBarEditKind.TrimEnd ? source.Start :
                source.Start + OrchestrationTime.FromSeconds(localStart - originalStart);
            OrchestrationTime candidateEnd = m_EditKind == BtsmtlTimeBarEditKind.TrimStart ? source.End :
                source.End + OrchestrationTime.FromSeconds(localEnd - originalEnd);
            m_Ranges[m_EditSlot] = new TimelineClipTimeRange(candidateStart,
                candidateEnd, source.EaseIn, source.EaseOut, source.ClipIn,
                source.SourceStart, source.SourceEnd, source.OtherEaseIn, source.OtherEaseOut);
            if (!m_Edit.TrySetRanges(m_Ranges, Stopwatch.GetTimestamp(), out message))
            {
                EditError = message;
                return false;
            }
            m_Edit.NotifyControlsSynchronized();
            m_EditChanged = !source.Equals(m_Ranges[m_EditSlot]);
            item.Start = start;
            item.End = end;
            foreach (BtsmtlTimeBarItem affected in m_Items)
                affected.Pending = !ReferenceEquals(affected, item) && affected.End >= m_EditStart;
            foreach (BtsmtlTimeBarRow row in m_Rows)
                row.Pending = false;
            foreach (BtsmtlTimeBarItem affected in m_Items)
                if (affected.Pending) m_Rows[affected.RowIndex].Pending = true;
            message = startMessage ?? endMessage;
            return true;
        }

        public override void CompleteEdit()
        {
            if (!m_EditChanged)
            {
                CancelEdit();
                return;
            }
            m_Edit.CompleteEdit();
            m_Edit = null;
            m_AwaitingResult = true;
        }

        public override void CancelEdit()
        {
            m_Edit?.CancelEdit();
            m_Edit = null;
            RestoreCompleteResult();
        }

        void RestoreCompleteResult()
        {
            if (m_Edited != null)
            {
                m_Edited.Start = m_EditStart;
                m_Edited.End = m_EditEnd;
            }
            foreach (BtsmtlTimeBarItem item in m_Items)
                item.Pending = item.SourceHighlighted = false;
            foreach (BtsmtlTimeBarRow row in m_Rows)
                row.Pending = false;
            m_Edited = null;
            m_AwaitingResult = false;
        }

        static bool SameSource(in RuntimeSourceElementKey left, in RuntimeSourceElementKey right) =>
            left.TimelineAuthoringId == right.TimelineAuthoringId && left.TrackAuthoringId == right.TrackAuthoringId &&
            left.ClipAuthoringId == right.ClipAuthoringId;

        public override void Navigate(BtsmtlTimeBarItem item)
        {
            RuntimeExecutionSpan span = ((SpanContext)item.Context).Span;
            if (m_Authoring != null) m_Authoring.Navigate(in span);
            else m_Open(span);
        }

        static bool ProvidingAt(SpanContext provider, SpanContext marker)
        {
            RuntimeExecutionSpan span = provider.Span;
            RuntimeExecutionSpan selected = marker.Span;
            return new StateKey(in span).Equals(new StateKey(marker)) && selected.StartPosition >= span.StartPosition &&
                (span.IsOpen || selected.StartPosition <= span.EndPosition);
        }

        public override void AddNavigationActions(BtsmtlTimeBarItem item, GenericMenu menu)
        {
            if (item.Kind != BtsmtlTimeBarItemKind.Transition)
                return;
            SpanContext marker = (SpanContext)item.Context;
            var added = new HashSet<RuntimeSourceElementKey>();
            foreach (BtsmtlTimeBarItem candidate in m_Items)
            {
                if (candidate.Kind != BtsmtlTimeBarItemKind.Provider) continue;
                SpanContext provider = (SpanContext)candidate.Context;
                if (provider.Span.Domain != RuntimeTraceDomain.Logic || !ProvidingAt(provider, marker) ||
                    !added.Add(provider.Span.Source))
                    continue;
                BtsmtlTimeBarItem target = candidate;
                menu.AddItem(new GUIContent("定位提供所读状态的 Clip/" + candidate.Label), false, () => Navigate(target));
            }
        }

        public void Clear()
        {
            CancelEdit();
            m_Rows.Clear();
            m_Items.Clear();
            m_End = 0;
            EditError = null;
            CaptureId = Guid.Empty;
            m_Revision++;
        }
    }
}
