using System;
using Photon.Deterministic;

namespace BTSMTL.Timeline
{
    public static class TimelineTimeGrid
    {
        public static FP Position(int index, int rate)
        {
            if (rate <= 0)
                throw new ArgumentOutOfRangeException(nameof(rate));
            long numerator = checked((long)index * FP._1.RawValue);
            long raw = numerator / rate;
            long remainder = numerator % rate;
            long twiceRemainder = Math.Abs(remainder) * 2;
            if (twiceRemainder > rate || twiceRemainder == rate && (raw & 1) != 0)
                raw += Math.Sign(remainder);
            return FP.FromRaw(raw);
        }

        public static int NearestIndex(FP time, int rate)
        {
            if (rate <= 0)
                throw new ArgumentOutOfRangeException(nameof(rate));
            return checked((int)decimal.Round((decimal)time.RawValue * rate / FP._1.RawValue, 0, MidpointRounding.ToEven));
        }

        public static int CeilingIndex(FP time, int rate)
        {
            int nearest = NearestIndex(time, rate);
            if (Position(nearest, rate) == time)
                return nearest;
            return checked((int)decimal.Ceiling((decimal)time.RawValue * rate / FP._1.RawValue));
        }
    }
}
