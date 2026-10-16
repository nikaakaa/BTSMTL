using System;
using Photon.Deterministic;
using ThirdPersonSimulation.QuantumNumerics;
using FPCurveData = ThirdPersonSimulation.GameplayAbilityCurve<Photon.Deterministic.FP>;

namespace ThirdPersonSimulation
{
    public readonly struct TimelineMotionSourceIntervalFP
    {
        public TimelineMotionSourceIntervalFP(FP previousSourceTime, FP sourceTime, FP weight, FP normalizedTime)
        {
            PreviousSourceTime = previousSourceTime;
            SourceTime = sourceTime;
            Weight = weight;
            NormalizedTime = normalizedTime;
        }

        public FP PreviousSourceTime { get; }
        public FP SourceTime { get; }
        public FP Weight { get; }
        public FP NormalizedTime { get; }
    }

    public readonly struct TimelineMotionPositionFP
    {
        public TimelineMotionPositionFP(FP x, FP y, FP z, FP yaw)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }

        public FP X { get; }
        public FP Y { get; }
        public FP Z { get; }
        public FP Yaw { get; }

        public static TimelineMotionPositionFP AccumulatePrefix(
            in TimelineMotionPositionFP previous,
            in TimelineMotionPositionFP current,
            in TimelineMotionPositionFP cycleStart,
            in TimelineMotionPositionFP cycleEnd,
            int cycleCount)
        {
            FP cycles = cycleCount;
            return new TimelineMotionPositionFP(
                current.X - previous.X + (cycleEnd.X - cycleStart.X) * cycles,
                current.Y - previous.Y + (cycleEnd.Y - cycleStart.Y) * cycles,
                current.Z - previous.Z + (cycleEnd.Z - cycleStart.Z) * cycles,
                current.Yaw - previous.Yaw + (cycleEnd.Yaw - cycleStart.Yaw) * cycles);
        }
    }

    public readonly struct TimelineMotionSampleFP
    {
        public TimelineMotionSampleFP(FP displacementX, FP displacementY, FP displacementZ,
            FP yawDegrees, FP weight, FP normalizedTime)
        {
            DisplacementX = displacementX;
            DisplacementY = displacementY;
            DisplacementZ = displacementZ;
            YawDegrees = yawDegrees;
            Weight = weight;
            NormalizedTime = normalizedTime;
        }

        public FP DisplacementX { get; }
        public FP DisplacementY { get; }
        public FP DisplacementZ { get; }
        public FP YawDegrees { get; }
        public FP Weight { get; }
        public FP NormalizedTime { get; }
        public bool HasDelta => DisplacementX != FP._0 || DisplacementY != FP._0 ||
                                DisplacementZ != FP._0 || YawDegrees != FP._0;
    }

    public sealed class TimelineMotionSamplingFP
    {
        readonly FPCurveData m_X;
        readonly FPCurveData m_Y;
        readonly FPCurveData m_Z;
        readonly FPCurveData m_Yaw;

        public FPCurveData X => m_X;
        public FPCurveData Y => m_Y;
        public FPCurveData Z => m_Z;
        public FPCurveData Yaw => m_Yaw;

        public TimelineMotionSamplingFP(FPCurveData x, FPCurveData y, FPCurveData z, FPCurveData yaw)
        {
            m_X = x;
            m_Y = y;
            m_Z = z;
            m_Yaw = yaw;
        }

        public TimelineMotionPositionFP EvaluateSource(FP sourceTime)
        {
            return new TimelineMotionPositionFP(
                EvaluateCurve(m_X, sourceTime), EvaluateCurve(m_Y, sourceTime),
                EvaluateCurve(m_Z, sourceTime), EvaluateCurve(m_Yaw, sourceTime));
        }

        public bool TrySampleSource(in TimelineMotionSourceIntervalFP interval,
            SimulationMotionBlendMode blendMode, bool consumeLowerChannels, out TimelineMotionSampleFP sample)
        {
            TimelineMotionPositionFP previous = EvaluateSource(interval.PreviousSourceTime);
            TimelineMotionPositionFP current = EvaluateSource(interval.SourceTime);
            sample = new TimelineMotionSampleFP(
                current.X - previous.X, current.Y - previous.Y, current.Z - previous.Z,
                current.Yaw - previous.Yaw, interval.Weight, interval.NormalizedTime);
            return sample.HasDelta || blendMode == SimulationMotionBlendMode.Override && consumeLowerChannels;
        }

        public int Sample(ReadOnlySpan<TimelineMotionSourceIntervalFP> intervals, int traversalIntervalCount,
            SimulationMotionBlendMode blendMode, bool consumeLowerChannels,
            Span<TimelineMotionSampleFP> samples)
        {
            int count = 0;
            TimelineMotionSampleFP last = default;
            FP x = FP._0;
            FP y = FP._0;
            FP z = FP._0;
            FP yaw = FP._0;
            bool sampled = false;
            for (int index = 0; index < intervals.Length; index++)
            {
                ref readonly TimelineMotionSourceIntervalFP interval = ref intervals[index];
                if (!TrySampleSource(in interval, blendMode,
                        consumeLowerChannels, out TimelineMotionSampleFP sample))
                    continue;
                if (traversalIntervalCount == 1 || blendMode != SimulationMotionBlendMode.Override)
                {
                    samples[count++] = sample;
                    continue;
                }
                last = sample;
                sampled = true;
                x += sample.DisplacementX * sample.Weight;
                y += sample.DisplacementY * sample.Weight;
                z += sample.DisplacementZ * sample.Weight;
                yaw += sample.YawDegrees * sample.Weight;
            }
            if (sampled)
                samples[count++] = new TimelineMotionSampleFP(x, y, z, yaw, FP._1, last.NormalizedTime);
            return count;
        }

        static FP EvaluateCurve(FPCurveData curve, FP time)
        {
            FP first = curve.Keys[0].Time;
            FP last = curve.Keys[curve.Keys.Count - 1].Time;
            if (first != last && (time < first || time > last))
            {
                int wrap = time < first ? curve.PreWrapMode : curve.PostWrapMode;
                if (wrap == 2 || wrap == 4)
                {
                    long duration = (last - first).RawValue;
                    long period = wrap == 4 ? checked(duration * 2) : duration;
                    long local = (time.RawValue - first.RawValue) % period;
                    if (local < 0) local += period;
                    if (wrap == 4 && local > duration) local = period - local;
                    time = FP.FromRaw(first.RawValue + local);
                }
            }
            return curve.Evaluate<QuantumSimulationMath>(time, FP._0);
        }
    }
}
