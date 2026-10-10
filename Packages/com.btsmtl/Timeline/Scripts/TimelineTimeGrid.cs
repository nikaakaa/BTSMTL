using System;
using ThirdPersonSimulation.Fixed;

namespace BTSMTL.Timeline
{
    public static class TimelineTimeGrid
    {
        public static FixedScalar Position(int index, int rate)
        {
            if (rate <= 0)
                throw new ArgumentOutOfRangeException(nameof(rate));
            return FixedScalar.FromRatio(index, rate);
        }

        public static int NearestIndex(FixedScalar time, int rate)
        {
            if (rate <= 0)
                throw new ArgumentOutOfRangeException(nameof(rate));
            return checked((int)decimal.Round((decimal)time.Raw * rate / FixedScalar.OneRaw, 0, MidpointRounding.ToEven));
        }

        public static int CeilingIndex(FixedScalar time, int rate)
        {
            int nearest = NearestIndex(time, rate);
            if (Position(nearest, rate) == time)
                return nearest;
            return checked((int)decimal.Ceiling((decimal)time.Raw * rate / FixedScalar.OneRaw));
        }
    }
}
