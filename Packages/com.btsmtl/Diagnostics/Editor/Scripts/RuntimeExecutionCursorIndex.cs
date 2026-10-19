using System;
using System.Collections.Generic;

namespace BTSMTL.Diagnostics.Editor
{
    public readonly struct RuntimeExecutionPlaybackKey : IEquatable<RuntimeExecutionPlaybackKey>
    {
        public RuntimeExecutionPlaybackKey(RuntimeInstanceKey instance, Guid branch, ulong epoch,
            RuntimeContentRevision revision, RuntimeTraceDomain domain)
        {
            Character = instance.CharacterRuntimeId;
            Action = instance.ActionInstanceId;
            Operation = instance.SourceOperationIndex;
            Playback = instance.TimelinePlaybackId;
            Branch = branch;
            Epoch = epoch;
            Revision = revision;
            Domain = domain == RuntimeTraceDomain.Lifecycle ? RuntimeTraceDomain.Logic : domain;
        }
        public Guid Character { get; }
        public ulong Action { get; }
        public int Operation { get; }
        public ulong Playback { get; }
        public Guid Branch { get; }
        public ulong Epoch { get; }
        public RuntimeContentRevision Revision { get; }
        public RuntimeTraceDomain Domain { get; }
        public bool IsValid => Playback != 0;
        public bool SamePlayback(in RuntimeExecutionPlaybackKey other) => Character == other.Character &&
            Action == other.Action && Operation == other.Operation && Playback == other.Playback &&
            Branch == other.Branch && Epoch == other.Epoch && Revision.Equals(other.Revision);
        public bool Equals(RuntimeExecutionPlaybackKey other) => SamePlayback(in other) && Domain == other.Domain;
        public override bool Equals(object obj) => obj is RuntimeExecutionPlaybackKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Character, Action, Operation, Playback, Branch,
            Epoch, Revision, (int)Domain);
    }

    public readonly struct RuntimeExecutionCursorSample
    {
        internal RuntimeExecutionCursorSample(in RuntimeTraceEvent value)
        {
            SessionTime = value.TimeSeconds;
            Cursor = value.Payload.Time;
            Cycle = value.Payload.Cycle;
            Position = value.Position;
            Sequence = value.Sequence;
            SectionId = value.Payload.RelatedElementId;
        }
        public double SessionTime { get; }
        public double Cursor { get; }
        public int Cycle { get; }
        public ulong Position { get; }
        public ulong Sequence { get; }
        public string SectionId { get; }
    }

    public readonly struct RuntimeExecutionCursorJump
    {
        internal RuntimeExecutionCursorJump(in RuntimeTraceEvent value)
        {
            SessionTime = value.TimeSeconds;
            Before = value.Payload.SecondaryTime;
            After = value.Payload.Time;
            SourceSection = value.Payload.Detail;
            TargetSection = value.Payload.RelatedElementId;
            Position = value.Position;
        }
        public double SessionTime { get; }
        public double Before { get; }
        public double After { get; }
        public string SourceSection { get; }
        public string TargetSection { get; }
        public ulong Position { get; }
    }

    public sealed class RuntimeExecutionPlaybackCursors
    {
        internal RuntimeExecutionPlaybackCursors(RuntimeTraceDomain domain) => Domain = domain;
        public RuntimeTraceDomain Domain { get; }
        internal readonly List<RuntimeExecutionCursorSample> SampleList = new();
        internal readonly List<RuntimeExecutionCursorJump> JumpList = new();
        public IReadOnlyList<RuntimeExecutionCursorSample> Samples => SampleList;
        public IReadOnlyList<RuntimeExecutionCursorJump> Jumps => JumpList;

        public bool TryMap(double sessionTime, double duration, bool preferAfterJump,
            out double localTime, out string message)
        {
            localTime = 0;
            if (SampleList.Count == 0 || sessionTime < SampleList[0].SessionTime ||
                sessionTime > SampleList[SampleList.Count - 1].SessionTime)
            {
                message = "该次播放缺少覆盖拖动位置的逐 Tick 游标记录。";
                return false;
            }
            int first = 0, end = SampleList.Count;
            while (first < end)
            {
                int middle = first + (end - first) / 2;
                if (SampleList[middle].SessionTime < sessionTime) first = middle + 1;
                else end = middle;
            }
            RuntimeExecutionCursorSample right = SampleList[first];
            RuntimeExecutionCursorSample left = first == 0 ? right : SampleList[first - 1];
            bool snapped = false;
            for (int i = 0; i < JumpList.Count; i++)
            {
                RuntimeExecutionCursorJump jump = JumpList[i];
                if (jump.SessionTime != sessionTime)
                    continue;
                localTime = preferAfterJump ? jump.After : jump.Before;
                snapped = true;
                if (!preferAfterJump)
                    break;
            }
            if (snapped)
            {
                message = "分段跳转处已吸附到所选一侧的游标。";
                return true;
            }
            if (right.SessionTime == sessionTime || first == 0)
            {
                localTime = right.Cursor;
                message = null;
                return true;
            }
            if (right.Position > left.Position + 1)
            {
                message = "两侧游标记录不连续，缺少中间 Tick，不能换算作者时间。";
                return false;
            }
            double segmentStart = left.SessionTime, segmentStartValue = left.Cursor;
            double segmentEnd = right.SessionTime, segmentEndValue = right.Cursor;
            bool split = false;
            for (int i = 0; i < JumpList.Count; i++)
            {
                RuntimeExecutionCursorJump jump = JumpList[i];
                if (jump.SessionTime < left.SessionTime || jump.SessionTime > right.SessionTime)
                    continue;
                split = true;
                if (jump.SessionTime < sessionTime)
                {
                    segmentStart = jump.SessionTime;
                    segmentStartValue = jump.After;
                }
                else
                {
                    segmentEnd = jump.SessionTime;
                    segmentEndValue = jump.Before;
                    break;
                }
            }
            if (split)
            {
                localTime = Interpolate(segmentStart, segmentStartValue, segmentEnd, segmentEndValue, sessionTime);
                message = null;
                return true;
            }
            double leftTime = left.Cursor + left.Cycle * duration;
            double rightTime = right.Cursor + right.Cycle * duration;
            double unwrapped = Interpolate(left.SessionTime, leftTime, right.SessionTime, rightTime, sessionTime);
            int cycle = right.Cycle == left.Cycle ? left.Cycle : (int)Math.Floor(unwrapped / duration);
            localTime = unwrapped - cycle * duration;
            message = leftTime == rightTime ? "该处为游标停顿区间，已吸附到停顿值。" : null;
            return true;
        }

        static double Interpolate(double firstTime, double firstValue, double lastTime, double lastValue, double time) =>
            firstTime == lastTime ? lastValue : firstValue + (lastValue - firstValue) * ((time - firstTime) / (lastTime - firstTime));
    }

    public sealed class RuntimeExecutionCursorIndex
    {
        readonly Dictionary<RuntimeExecutionPlaybackKey, RuntimeExecutionPlaybackCursors> m_Playbacks = new();
        public IReadOnlyDictionary<RuntimeExecutionPlaybackKey, RuntimeExecutionPlaybackCursors> Playbacks => m_Playbacks;

        internal void Rebuild(RuntimeExecutionTimeline timeline)
        {
            m_Playbacks.Clear();
            for (int i = 0; i < timeline.Spans.Count; i++)
            {
                ref readonly RuntimeCaptureSpanData span = ref timeline.GetSpanData(i);
                if (span.Start.Kind is not RuntimeTraceEventKind.TimelineLogicTime and not RuntimeTraceEventKind.TimelineVisualTime)
                    continue;
                var key = new RuntimeExecutionPlaybackKey(span.Start.RuntimeInstance, span.Start.ExecutionBranchId,
                    span.Start.RuntimeEpoch, span.Start.ContentRevision, span.Start.Domain);
                if (!m_Playbacks.TryGetValue(key, out RuntimeExecutionPlaybackCursors cursors))
                {
                    cursors = new RuntimeExecutionPlaybackCursors(span.Start.Domain);
                    m_Playbacks.Add(key, cursors);
                }
                cursors.SampleList.Add(new RuntimeExecutionCursorSample(in span.Start));
            }
            foreach (RuntimeExecutionPlaybackCursors cursors in m_Playbacks.Values)
                cursors.SampleList.Sort((left, right) => left.SessionTime != right.SessionTime
                    ? left.SessionTime.CompareTo(right.SessionTime) : left.Sequence.CompareTo(right.Sequence));
            for (int i = 0; i < timeline.Spans.Count; i++)
            {
                ref readonly RuntimeCaptureSpanData span = ref timeline.GetSpanData(i);
                if (span.Start.Kind != RuntimeTraceEventKind.TimelineSectionJumped)
                    continue;
                var jumpKey = new RuntimeExecutionPlaybackKey(span.Start.RuntimeInstance, span.Start.ExecutionBranchId,
                    span.Start.RuntimeEpoch, span.Start.ContentRevision, span.Start.Domain);
                foreach (var entry in m_Playbacks)
                    if (entry.Key.SamePlayback(in jumpKey))
                        entry.Value.JumpList.Add(new RuntimeExecutionCursorJump(in span.Start));
            }
            foreach (RuntimeExecutionPlaybackCursors cursors in m_Playbacks.Values)
                cursors.JumpList.Sort((left, right) => left.SessionTime.CompareTo(right.SessionTime));
        }

        public bool TryGet(in RuntimeExecutionPlaybackKey key, out RuntimeExecutionPlaybackCursors cursors) =>
            m_Playbacks.TryGetValue(key, out cursors);
    }
}
