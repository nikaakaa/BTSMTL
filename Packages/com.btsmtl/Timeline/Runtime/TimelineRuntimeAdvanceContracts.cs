using System;
using System.Runtime.CompilerServices;
using ThirdPersonSimulation.Fixed;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public readonly struct TimelineRuntimePlaybackHandle : IEquatable<TimelineRuntimePlaybackHandle>
    {
        public TimelineRuntimePlaybackHandle(ulong value)
        {
            if (value == 0)
                throw new ArgumentOutOfRangeException(nameof(value));
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(TimelineRuntimePlaybackHandle other) => Value == other.Value;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object obj) => obj is TimelineRuntimePlaybackHandle other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public static TimelineRuntimePlaybackHandle Invalid => default;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(TimelineRuntimePlaybackHandle left, TimelineRuntimePlaybackHandle right) => left.Equals(right);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(TimelineRuntimePlaybackHandle left, TimelineRuntimePlaybackHandle right) => !left.Equals(right);
    }

    public readonly struct TimelineRuntimeAdvanceRequest
    {
        public TimelineRuntimeAdvanceRequest(ulong logicTick, FixedScalar previousTime, FixedScalar targetTime, int timeCarry, AbilityTimelinePlaybackControl control)
        {
            if (logicTick == 0)
                throw new ArgumentOutOfRangeException(nameof(logicTick));
            if (previousTime < FixedScalar.Zero || targetTime < previousTime)
                throw new ArgumentOutOfRangeException(nameof(targetTime));
            LogicTick = logicTick;
            PreviousTime = previousTime;
            TargetTime = targetTime;
            TimeCarry = timeCarry;
            Control = control;
        }

        public ulong LogicTick { get; }
        public FixedScalar PreviousTime { get; }
        public FixedScalar TargetTime { get; }
        public int TimeCarry { get; }
        public AbilityTimelinePlaybackControl Control { get; }
    }

    public enum TimelineRuntimeClipBoundaryKind : byte
    {
        Exit = 0,
        Enter = 1
    }

    public readonly struct TimelineRuntimeClipBoundary
    {
        public TimelineRuntimeClipBoundary(
            string authoringId,
            string trackAuthoringId,
            FixedScalar time,
            int cycle,
            TimelineRuntimeClipBoundaryKind kind)
        {
            AuthoringId = string.IsNullOrWhiteSpace(authoringId)
                ? throw new ArgumentException("Timeline Clip identity is required.", nameof(authoringId))
                : authoringId.Trim();
            TrackAuthoringId = string.IsNullOrWhiteSpace(trackAuthoringId)
                ? throw new ArgumentException("Timeline Track identity is required.", nameof(trackAuthoringId))
                : trackAuthoringId.Trim();
            if (time < FixedScalar.Zero)
                throw new ArgumentOutOfRangeException(nameof(time));
            if (cycle < 0)
                throw new ArgumentOutOfRangeException(nameof(cycle));
            if (kind != TimelineRuntimeClipBoundaryKind.Exit && kind != TimelineRuntimeClipBoundaryKind.Enter)
                throw new ArgumentOutOfRangeException(nameof(kind));
            Time = time;
            Cycle = cycle;
            Kind = kind;
        }

        public string AuthoringId { get; }
        public string TrackAuthoringId { get; }
        public FixedScalar Time { get; }
        public int Cycle { get; }
        public TimelineRuntimeClipBoundaryKind Kind { get; }
    }

    public readonly struct TimelineRuntimeAdvanceResult
    {
        internal TimelineRuntimeAdvanceResult(
            TimelineRuntimePlayback owner,
            ulong advanceSequence,
            ulong generation,
            TimelineRuntimeAdvanceRequest request,
            FixedScalar previousTime,
            FixedScalar time,
            int previousCycle,
            int cycle,
            int timeCarry,
            string sectionId,
            TimelineRuntimeSampleView<CanonicalTextValue> activeClipIds,
            TimelineRuntimeSampleView<TimelineRuntimeClipBoundary> boundaries,
            TimelineRuntimeEvaluationResult evaluation,
            bool completes)
        {
            Owner = owner;
            AdvanceSequence = advanceSequence;
            Generation = generation;
            Request = request;
            PreviousTime = previousTime;
            Time = time;
            PreviousCycle = previousCycle;
            Cycle = cycle;
            TimeCarry = timeCarry;
            SectionId = sectionId ?? string.Empty;
            ActiveClipIds = activeClipIds;
            Boundaries = boundaries;
            Evaluation = evaluation;
            Completes = completes;
        }

        public bool IsValid => Owner != null;
        internal ulong AdvanceSequence { get; }
        internal TimelineRuntimePlayback Owner { get; }
        internal TimelineRuntimeAdvanceRequest Request { get; }
        internal int TimeCarry { get; }
        public ulong Generation { get; }
        public string ContentIdentity => Owner.Content.Identity;
        public CanonicalTextValue ContentRevision => Owner.Content.Revision;
        public FixedScalar Duration => Owner.Content.Duration;
        public TimelinePlaybackMode PlaybackMode => Owner.PlaybackMode;
        public ulong LogicTick => Request.LogicTick;
        public AbilityTimelinePlaybackControl Control => Request.Control;
        public FixedScalar PreviousTime { get; }
        public FixedScalar Time { get; }
        public int PreviousCycle { get; }
        public int Cycle { get; }
        public string SectionId { get; }
        public TimelineRuntimeSampleView<CanonicalTextValue> ActiveClipIds { get; }
        public TimelineRuntimeSampleView<TimelineRuntimeClipBoundary> Boundaries { get; }
        public readonly TimelineRuntimeEvaluationResult Evaluation;
        public bool Completes { get; }
    }
}
