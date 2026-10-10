using System;

namespace ThirdPersonSimulation.Collision
{
    public static class SweepDataGenerator
    {
        public static int WriteTrack<T>(SweepTrack track, ReadOnlySpan<SweepSample<T>> samples, Span<SweepSegment<T>> destination) where T : struct, IComparable<T>
        {
            int count = samples.Length == 1 ? 1 : Math.Max(0, samples.Length - 1);
            if (destination.Length < count)
                throw new InvalidOperationException("Sweep segment capacity exceeded.");
            if (samples.Length == 1)
            {
                destination[0] = new SweepSegment<T>(track, samples[0], samples[0]);
                return 1;
            }
            for (int i = 0; i < count; i++)
            {
                if (samples[i].Time.CompareTo(samples[i + 1].Time) >= 0)
                    throw new ArgumentException("Sweep track sample times must increase.", nameof(samples));
                destination[i] = new SweepSegment<T>(track, samples[i], samples[i + 1]);
            }
            return count;
        }
    }
}
