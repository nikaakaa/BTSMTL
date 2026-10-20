using System;
using System.Collections.Generic;
using BTSMTL.Diagnostics;
using BTSMTL.Diagnostics.Editor;

namespace BTSMTL.Timeline.Editor
{
    // 一次状态激活的正式身份：运行实例、作者状态、激活代次、动作实例、技能执行代次、历史分支与纪元。
    public readonly struct BtsmtlExecutionStateIdentity : IEquatable<BtsmtlExecutionStateIdentity>
    {
        BtsmtlExecutionStateIdentity(Guid runtime, string graph, string state, ulong generation, ulong action,
            ulong execution, Guid branch, ulong epoch)
        {
            GraphRuntimeId = runtime;
            GraphAuthoringId = graph ?? string.Empty;
            StateId = state ?? string.Empty;
            ActivationGeneration = generation;
            ActionInstanceId = action;
            SkillExecutionGeneration = execution;
            ExecutionBranchId = branch;
            RuntimeEpoch = epoch;
        }

        public Guid GraphRuntimeId { get; }
        public string GraphAuthoringId { get; }
        public string StateId { get; }
        public ulong ActivationGeneration { get; }
        public ulong ActionInstanceId { get; }
        public ulong SkillExecutionGeneration { get; }
        public Guid ExecutionBranchId { get; }
        public ulong RuntimeEpoch { get; }

        internal static BtsmtlExecutionStateIdentity OfState(in RuntimeExecutionSpan state) => new(
            state.Instance.GraphRuntimeId, state.Source.GraphAuthoringId, state.Source.ElementAuthoringId,
            state.ActivationGeneration, state.ActionInstanceId, state.SkillExecutionGeneration,
            state.ExecutionBranchId, state.RuntimeEpoch);

        // 状态转移事件由源状态操作发出：OwnerId 是源状态节点，代次是源状态本次激活。
        internal static BtsmtlExecutionStateIdentity OfTransition(in RuntimeExecutionSpan transition) => new(
            transition.Instance.GraphRuntimeId, transition.Source.GraphAuthoringId, transition.Start.Payload.OwnerId,
            transition.ActivationGeneration, transition.ActionInstanceId, transition.SkillExecutionGeneration,
            transition.ExecutionBranchId, transition.RuntimeEpoch);

        internal static BtsmtlExecutionStateIdentity OfPlayback(in RuntimeExecutionSpan playback)
        {
            RuntimeTimelinePlaybackProvenance provenance = playback.Start.Payload.TimelinePlayback;
            return new(provenance.StateMachineGraphRuntimeId, provenance.StateMachineGraphAuthoringId,
                provenance.StateId, provenance.StateActivationGeneration, playback.ActionInstanceId,
                playback.SkillExecutionGeneration, playback.ExecutionBranchId, playback.RuntimeEpoch);
        }

        public bool Equals(BtsmtlExecutionStateIdentity other) => GraphRuntimeId == other.GraphRuntimeId &&
            GraphAuthoringId == other.GraphAuthoringId && StateId == other.StateId &&
            ActivationGeneration == other.ActivationGeneration && ActionInstanceId == other.ActionInstanceId &&
            SkillExecutionGeneration == other.SkillExecutionGeneration && ExecutionBranchId == other.ExecutionBranchId &&
            RuntimeEpoch == other.RuntimeEpoch;
        public override bool Equals(object obj) => obj is BtsmtlExecutionStateIdentity other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(GraphRuntimeId, GraphAuthoringId, StateId,
            ActivationGeneration, ActionInstanceId, SkillExecutionGeneration, ExecutionBranchId, RuntimeEpoch);
    }

    // 真实档执行记录投影：按状态激活身份归属 Timeline 播放并按圈拆开，保留正式选中的状态转移；
    // 时间为执行时间线的会话相对时刻。来源身份缺失或不唯一的播放不归属任何状态，不进入路线。
    public sealed class BtsmtlExecutionRecord
    {
        public const RuntimeTraceChannel TraceChannels = RuntimeTraceChannel.Graph |
            RuntimeTraceChannel.StateMachine | RuntimeTraceChannel.Timeline;

        sealed class PlaybackGroup
        {
            internal RuntimeExecutionSpan Source;
            internal RuntimeExecutionPlaybackCursors Cursors;
            internal double Start = double.PositiveInfinity;
            internal double End;
        }

        public readonly struct RecordedPlayback
        {
            internal RecordedPlayback(in RuntimeExecutionSpan span, double start, double end, double localStart,
                double localEnd, int cycle, bool hasLocal, RuntimeExecutionPlaybackCursors cursors, double cursorOrigin)
            {
                Owner = BtsmtlExecutionStateIdentity.OfPlayback(in span);
                PlaybackId = span.Instance.TimelinePlaybackId;
                CharacterRuntimeId = span.Instance.CharacterRuntimeId;
                TimelineAuthoringId = span.Source.TimelineAuthoringId;
                RuntimeTimelinePlaybackProvenance provenance = span.Start.Payload.TimelinePlayback;
                OperationIndex = provenance.SourceOperationIndex;
                ActivationGeneration = provenance.SourceActivationGeneration;
                CallerPlaybackId = provenance.CallerPlaybackId;
                CallerClipId = provenance.CallerClipAuthoringId;
                Start = start;
                End = end;
                LocalStart = localStart;
                LocalEnd = localEnd;
                Cycle = cycle;
                HasLocal = hasLocal;
                Cursors = cursors;
                CursorOrigin = cursorOrigin;
            }

            public BtsmtlExecutionStateIdentity Owner { get; }
            public ulong PlaybackId { get; }
            public Guid CharacterRuntimeId { get; }
            public string TimelineAuthoringId { get; }
            public int OperationIndex { get; }
            public ulong ActivationGeneration { get; }
            public ulong CallerPlaybackId { get; }
            public string CallerClipId { get; }
            public bool IsNested => CallerPlaybackId != 0;

            public bool IsCalledBy(in RecordedPlayback other) => IsNested &&
                CharacterRuntimeId == other.CharacterRuntimeId && Owner.ActionInstanceId == other.Owner.ActionInstanceId &&
                Owner.SkillExecutionGeneration == other.Owner.SkillExecutionGeneration &&
                Owner.ExecutionBranchId == other.Owner.ExecutionBranchId && Owner.RuntimeEpoch == other.Owner.RuntimeEpoch &&
                other.PlaybackId == CallerPlaybackId;
            public double Start { get; }
            public double End { get; }
            // 段内首末游标样本；缺少游标记录时 HasLocal 为 false。
            public double LocalStart { get; }
            public double LocalEnd { get; }
            public int Cycle { get; }
            public bool HasLocal { get; }
            // 该次播放的正式逐 Tick 游标；会话相对时刻加 CursorOrigin 得到样本时刻。
            public RuntimeExecutionPlaybackCursors Cursors { get; }
            public double CursorOrigin { get; }

            // 本圈内会话相对时刻 → 作者本地时刻；停顿吸附等说明写入 message，无记录时拒绝。
            public bool TryLocal(double time, double duration, out double local, out string message)
            {
                local = 0;
                if (Cursors == null)
                {
                    message = "该次播放没有逐 Tick 游标记录，不能换算作者时间。";
                    return false;
                }
                if (time < Start || time > End)
                {
                    message = $"会话第 {time:0.000} 秒不在这次播放第 {Cycle + 1} 圈内。";
                    return false;
                }
                return Cursors.TryMap(time + CursorOrigin, duration, out local, out message);
            }

            // 作者本地时刻 → 本圈内会话相对时刻；不在本圈记录内时拒绝，不外推。
            public bool TryGlobal(double local, double duration, out double time, out string message)
            {
                time = 0;
                if (Cursors == null)
                {
                    message = "该次播放没有逐 Tick 游标记录，不能换算会话时间。";
                    return false;
                }
                if (!Cursors.TryUnmap(local, Cycle, duration, out double session, out message))
                    return false;
                time = session - CursorOrigin;
                if (time >= Start && time <= End)
                    return true;
                message = $"本地 {local:0.000} 秒不在这次播放第 {Cycle + 1} 圈的记录内。";
                return false;
            }
        }

        // 正式选中的状态转移：源状态激活身份、转移连线的作者身份与发生时刻。目标由作者连线决定，不由下一段播放推断。
        public readonly struct RecordedTransition
        {
            internal RecordedTransition(in RuntimeExecutionSpan span, double time)
            {
                Source = BtsmtlExecutionStateIdentity.OfTransition(in span);
                GraphId = span.Source.GraphAuthoringId;
                EdgeId = span.Source.ElementAuthoringId;
                Time = time;
            }

            public BtsmtlExecutionStateIdentity Source { get; }
            public string GraphId { get; }
            public string EdgeId { get; }
            public double Time { get; }
        }

        readonly RuntimeDebugSession m_Session;
        readonly List<RecordedPlayback> m_Recorded = new();
        readonly List<RecordedTransition> m_Transitions = new();
        double m_LogicOrigin;
        double m_PresentationOrigin;

        public BtsmtlExecutionRecord(RuntimeDebugSession session) => m_Session = session;

        public IReadOnlyList<RecordedPlayback> Recorded => m_Recorded;
        public IReadOnlyList<RecordedTransition> Transitions => m_Transitions;

        public void Apply(RuntimeExecutionTimeline timeline)
        {
            m_Recorded.Clear();
            m_Transitions.Clear();
            m_LogicOrigin = m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Logic);
            m_PresentationOrigin = m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Presentation);
            var states = new HashSet<BtsmtlExecutionStateIdentity>();
            var ambiguous = new HashSet<BtsmtlExecutionStateIdentity>();
            var playbacks = new Dictionary<RuntimeExecutionPlaybackKey, PlaybackGroup>();
            for (int i = 0; i < timeline.Spans.Count; i++)
            {
                RuntimeExecutionSpan span = timeline.Spans[i];
                if (!m_Session.TryGetHistoryTimeRange(span.ExecutionBranchId, span.Domain,
                    span.Start.TimeSeconds, timeline.GetObservedEndTime(i), out double first, out double last))
                    continue;
                if (span.Kind == RuntimeExecutionSpanKind.State)
                {
                    BtsmtlExecutionStateIdentity state = BtsmtlExecutionStateIdentity.OfState(in span);
                    if (!states.Add(state))
                        ambiguous.Add(state);
                    continue;
                }
                if (span.Kind == RuntimeExecutionSpanKind.Transition)
                {
                    if (span.Source.Kind == RuntimeSourceElementKind.Edge)
                        m_Transitions.Add(new RecordedTransition(in span, first - Origin(span.Domain)));
                    continue;
                }
                if (span.Instance.TimelinePlaybackId == 0)
                    continue;
                var key = new RuntimeExecutionPlaybackKey(span.Instance, span.ExecutionBranchId,
                    span.RuntimeEpoch, span.ContentRevision, RuntimeTraceDomain.Logic);
                if (!playbacks.TryGetValue(key, out PlaybackGroup group))
                    playbacks.Add(key, group = new PlaybackGroup { Source = span });
                RuntimeExecutionPlaybackKey cursorKey = span.Playback;
                if ((group.Cursors == null || span.Domain == RuntimeTraceDomain.Logic) &&
                    timeline.Cursors.TryGet(in cursorKey, out RuntimeExecutionPlaybackCursors cursors) && cursors != null)
                    group.Cursors = cursors;
                double origin = Origin(span.Domain);
                group.Start = Math.Min(group.Start, first - origin);
                group.End = Math.Max(group.End, last - origin);
            }
            foreach (PlaybackGroup playback in playbacks.Values)
            {
                RuntimeTimelinePlaybackProvenance provenance = playback.Source.Start.Payload.TimelinePlayback;
                var owner = BtsmtlExecutionStateIdentity.OfPlayback(in playback.Source);
                if (!provenance.HasCaller && (!provenance.HasStateActivation || ambiguous.Contains(owner) || !states.Contains(owner)))
                    continue;
                Split(playback);
            }
            m_Recorded.Sort((left, right) => left.Start.CompareTo(right.Start));
            m_Transitions.Sort((left, right) => left.Time.CompareTo(right.Time));
        }

        public void Clear()
        {
            m_Recorded.Clear();
            m_Transitions.Clear();
        }

        void Split(PlaybackGroup playback)
        {
            RuntimeExecutionPlaybackCursors cursors = playback.Cursors;
            if (cursors == null || cursors.Samples.Count == 0)
            {
                Record(playback, null, playback.Start, playback.End, 0);
                return;
            }
            double origin = Origin(cursors.Domain);
            int cycle = cursors.Samples[0].Cycle;
            double start = playback.Start;
            for (int i = 1; i < cursors.Samples.Count; i++)
            {
                RuntimeExecutionCursorSample sample = cursors.Samples[i];
                if (sample.Cycle == cycle)
                    continue;
                double boundary = sample.SessionTime - origin;
                Record(playback, cursors, start, boundary, cycle);
                start = boundary;
                cycle = sample.Cycle;
            }
            Record(playback, cursors, start, playback.End, cycle);
        }

        void Record(PlaybackGroup playback, RuntimeExecutionPlaybackCursors cursors, double from, double to, int cycle)
        {
            double localStart = 0, localEnd = 0;
            bool hasLocal = false;
            double origin = cursors == null ? 0 : Origin(cursors.Domain);
            if (cursors != null)
            {
                foreach (RuntimeExecutionCursorSample sample in cursors.Samples)
                {
                    double time = sample.SessionTime - origin;
                    if (sample.Cycle != cycle || time < from || time > to)
                        continue;
                    if (!hasLocal)
                        localStart = sample.Cursor;
                    localEnd = sample.Cursor;
                    hasLocal = true;
                }
            }
            m_Recorded.Add(new RecordedPlayback(in playback.Source, from, to, localStart, localEnd, cycle, hasLocal,
                cursors, origin));
        }

        double Origin(RuntimeTraceDomain domain) =>
            domain == RuntimeTraceDomain.Presentation ? m_PresentationOrigin : m_LogicOrigin;
    }
}
