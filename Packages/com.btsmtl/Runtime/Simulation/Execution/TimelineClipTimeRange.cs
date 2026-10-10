using System;
using ThirdPersonSimulation;
using ThirdPersonSimulation.Fixed;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineClipTimeRange : IEquatable<TimelineClipTimeRange>
    {
        public TimelineClipTimeRange(FixedScalar start, FixedScalar end, FixedScalar easeIn,
            FixedScalar easeOut, FixedScalar clipIn, float sourceStart, float sourceEnd,
            FixedScalar otherEaseIn = default, FixedScalar otherEaseOut = default)
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

        public FixedScalar Start { get; }
        public FixedScalar End { get; }
        public FixedScalar EaseIn { get; }
        public FixedScalar EaseOut { get; }
        public FixedScalar ClipIn { get; }
        public float SourceStart { get; }
        public float SourceEnd { get; }
        public FixedScalar OtherEaseIn { get; }
        public FixedScalar OtherEaseOut { get; }
        public FixedScalar EffectiveEaseIn => OtherEaseIn == FixedScalar.Zero ? EaseIn : OtherEaseIn;
        public FixedScalar EffectiveEaseOut => OtherEaseOut == FixedScalar.Zero ? EaseOut : OtherEaseOut;

        public bool Equals(TimelineClipTimeRange other) => Start == other.Start && End == other.End &&
            EaseIn == other.EaseIn && EaseOut == other.EaseOut && ClipIn == other.ClipIn &&
            SourceStart == other.SourceStart && SourceEnd == other.SourceEnd &&
            OtherEaseIn == other.OtherEaseIn && OtherEaseOut == other.OtherEaseOut;

        public override bool Equals(object other) => other is TimelineClipTimeRange range && Equals(range);
        public override int GetHashCode() => HashCode.Combine(Start, End, EaseIn, EaseOut, ClipIn, SourceStart, SourceEnd,
            HashCode.Combine(OtherEaseIn, OtherEaseOut));

        public void Write(CanonicalWriter writer)
        {
            writer.WriteInt64(Start.Raw);
            writer.WriteInt64(End.Raw);
            writer.WriteInt64(EaseIn.Raw);
            writer.WriteInt64(EaseOut.Raw);
            writer.WriteInt64(ClipIn.Raw);
            writer.WriteInt32(BitConverter.SingleToInt32Bits(SourceStart));
            writer.WriteInt32(BitConverter.SingleToInt32Bits(SourceEnd));
            writer.WriteInt64(OtherEaseIn.Raw);
            writer.WriteInt64(OtherEaseOut.Raw);
        }

        public static TimelineClipTimeRange Read(CanonicalReader reader) => new(
            FixedScalar.FromRaw(reader.ReadInt64()), FixedScalar.FromRaw(reader.ReadInt64()),
            FixedScalar.FromRaw(reader.ReadInt64()), FixedScalar.FromRaw(reader.ReadInt64()),
            FixedScalar.FromRaw(reader.ReadInt64()), BitConverter.Int32BitsToSingle(reader.ReadInt32()),
            BitConverter.Int32BitsToSingle(reader.ReadInt32()), FixedScalar.FromRaw(reader.ReadInt64()),
            FixedScalar.FromRaw(reader.ReadInt64()));
    }
}
