using System;
using System.Collections.Generic;
using BTSMTL.Diagnostics;
using BTSMTL.Diagnostics.Editor;

namespace BTSMTL.Timeline.Editor
{
    // 真实档执行记录投影：按状态实例归属 Timeline 播放，按圈拆开，时间为执行时间线的会话相对时刻；
    // 来源身份缺失或不唯一的播放不归属任何状态，不进入路线。
    public sealed class BtsmtlExecutionRecord
    {
        public const RuntimeTraceChannel TraceChannels = RuntimeTraceChannel.Graph |
            RuntimeTraceChannel.StateMachine | RuntimeTraceChannel.Timeline;

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

            internal StateKey(in RuntimeExecutionSpan state)
            {
                m_Runtime = state.Instance.GraphRuntimeId;
                m_Graph = state.Source.GraphAuthoringId;
                m_State = state.Source.ElementAuthoringId;
                m_Generation = state.ActivationGeneration;
                m_Action = state.ActionInstanceId;
                m_Execution = state.SkillExecutionGeneration;
                m_Branch = state.ExecutionBranchId;
                m_Epoch = state.RuntimeEpoch;
            }

            internal StateKey(in RuntimeExecutionSpan playback, bool _)
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
                double localEnd, int cycle, bool hasLocal)
            {
                RuntimeTimelinePlaybackProvenance provenance = span.Start.Payload.TimelinePlayback;
                TimelineAuthoringId = span.Source.TimelineAuthoringId;
                StateMachineGraphId = provenance.StateMachineGraphAuthoringId;
                StateId = provenance.StateId;
                Start = start;
                End = end;
                LocalStart = localStart;
                LocalEnd = localEnd;
                Cycle = cycle;
                HasLocal = hasLocal;
            }

            public string TimelineAuthoringId { get; }
            public string StateMachineGraphId { get; }
            public string StateId { get; }
            public double Start { get; }
            public double End { get; }
            // 段内首末游标样本；缺少游标记录时 HasLocal 为 false。
            public double LocalStart { get; }
            public double LocalEnd { get; }
            public int Cycle { get; }
            public bool HasLocal { get; }
        }

        readonly RuntimeDebugSession m_Session;
        readonly List<RecordedPlayback> m_Recorded = new();
        double m_LogicOrigin;
        double m_PresentationOrigin;

        public BtsmtlExecutionRecord(RuntimeDebugSession session) => m_Session = session;

        public IReadOnlyList<RecordedPlayback> Recorded => m_Recorded;

        public void Apply(RuntimeExecutionTimeline timeline)
        {
            m_Recorded.Clear();
            m_LogicOrigin = m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Logic);
            m_PresentationOrigin = m_Session.GetExecutionTimeOrigin(RuntimeTraceDomain.Presentation);
            var states = new HashSet<StateKey>();
            var ambiguous = new HashSet<StateKey>();
            var playbacks = new Dictionary<RuntimeExecutionPlaybackKey, PlaybackGroup>();
            for (int i = 0; i < timeline.Spans.Count; i++)
            {
                RuntimeExecutionSpan span = timeline.Spans[i];
                if (!m_Session.TryGetHistoryTimeRange(span.ExecutionBranchId, span.Domain,
                    span.Start.TimeSeconds, timeline.GetObservedEndTime(i), out double first, out double last))
                    continue;
                if (span.Kind == RuntimeExecutionSpanKind.State)
                {
                    if (!states.Add(new StateKey(in span)))
                        ambiguous.Add(new StateKey(in span));
                    continue;
                }
                if (span.Kind == RuntimeExecutionSpanKind.Transition || span.Instance.TimelinePlaybackId == 0)
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
                if (!playback.Source.Start.Payload.TimelinePlayback.HasStateActivation)
                    continue;
                var owner = new StateKey(in playback.Source, true);
                if (ambiguous.Contains(owner) || !states.Contains(owner))
                    continue;
                Split(playback);
            }
            m_Recorded.Sort((left, right) => left.Start.CompareTo(right.Start));
        }

        public void Clear() => m_Recorded.Clear();

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
            if (cursors != null)
            {
                double origin = Origin(cursors.Domain);
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
            m_Recorded.Add(new RecordedPlayback(in playback.Source, from, to, localStart, localEnd, cycle, hasLocal));
        }

        double Origin(RuntimeTraceDomain domain) =>
            domain == RuntimeTraceDomain.Presentation ? m_PresentationOrigin : m_LogicOrigin;
    }
}
