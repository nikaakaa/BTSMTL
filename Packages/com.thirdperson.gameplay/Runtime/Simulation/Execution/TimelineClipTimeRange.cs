using System;
using ThirdPersonSimulation;
using Photon.Deterministic;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineClipTimeRange : IEquatable<TimelineClipTimeRange>
    {
        public TimelineClipTimeRange(FP start, FP end, FP easeIn,
            FP easeOut, FP clipIn, float sourceStart, float sourceEnd,
            FP otherEaseIn = default, FP otherEaseOut = default)
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

        public FP Start { get; }
        public FP End { get; }
        public FP EaseIn { get; }
        public FP EaseOut { get; }
        public FP ClipIn { get; }
        public float SourceStart { get; }
        public float SourceEnd { get; }
        public FP OtherEaseIn { get; }
        public FP OtherEaseOut { get; }
        public FP EffectiveEaseIn => OtherEaseIn == FP._0 ? EaseIn : OtherEaseIn;
        public FP EffectiveEaseOut => OtherEaseOut == FP._0 ? EaseOut : OtherEaseOut;

        public bool Equals(TimelineClipTimeRange other) => Start == other.Start && End == other.End &&
            EaseIn == other.EaseIn && EaseOut == other.EaseOut && ClipIn == other.ClipIn &&
            SourceStart == other.SourceStart && SourceEnd == other.SourceEnd &&
            OtherEaseIn == other.OtherEaseIn && OtherEaseOut == other.OtherEaseOut;

        public override bool Equals(object other) => other is TimelineClipTimeRange range && Equals(range);
        public override int GetHashCode() => HashCode.Combine(Start, End, EaseIn, EaseOut, ClipIn, SourceStart, SourceEnd,
            HashCode.Combine(OtherEaseIn, OtherEaseOut));




    }
}
