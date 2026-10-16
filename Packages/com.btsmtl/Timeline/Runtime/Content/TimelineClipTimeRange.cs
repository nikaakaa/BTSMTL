using BTSMTL.Orchestration;
using System;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineClipTimeRange : IEquatable<TimelineClipTimeRange>
    {
        public TimelineClipTimeRange(OrchestrationTime start, OrchestrationTime end, OrchestrationTime easeIn,
            OrchestrationTime easeOut, OrchestrationTime clipIn, float sourceStart, float sourceEnd,
            OrchestrationTime otherEaseIn = default, OrchestrationTime otherEaseOut = default)
        {
            Start = start;
            End = end;
            EaseIn = easeIn;
            EaseOut = easeOut;
            ClipIn = clipIn;
            SourceStart = sourceStart;
            SourceEnd = sourceEnd;
            OtherEaseIn = otherEaseIn;
            OtherEaseOut = otherEaseOut;
        }

        public OrchestrationTime Start { get; }
        public OrchestrationTime End { get; }
        public OrchestrationTime EaseIn { get; }
        public OrchestrationTime EaseOut { get; }
        public OrchestrationTime ClipIn { get; }
        public float SourceStart { get; }
        public float SourceEnd { get; }
        public OrchestrationTime OtherEaseIn { get; }
        public OrchestrationTime OtherEaseOut { get; }
        public OrchestrationTime EffectiveEaseIn => OtherEaseIn == OrchestrationTime.Zero ? EaseIn : OtherEaseIn;
        public OrchestrationTime EffectiveEaseOut => OtherEaseOut == OrchestrationTime.Zero ? EaseOut : OtherEaseOut;

        public bool Equals(TimelineClipTimeRange other) => Start == other.Start && End == other.End &&
            EaseIn == other.EaseIn && EaseOut == other.EaseOut && ClipIn == other.ClipIn &&
            SourceStart == other.SourceStart && SourceEnd == other.SourceEnd &&
            OtherEaseIn == other.OtherEaseIn && OtherEaseOut == other.OtherEaseOut;

        public override bool Equals(object other) => other is TimelineClipTimeRange range && Equals(range);
        public override int GetHashCode() => HashCode.Combine(Start, End, EaseIn, EaseOut, ClipIn, SourceStart, SourceEnd,
            HashCode.Combine(OtherEaseIn, OtherEaseOut));




    }
}
