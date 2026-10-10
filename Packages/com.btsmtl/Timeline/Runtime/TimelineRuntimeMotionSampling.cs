using System;
using System.Collections.Generic;
using ThirdPersonSimulation.Fixed;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    internal abstract class TimelineRuntimeCurve
    {
        public abstract FixedScalar Evaluate(FixedScalar time);
        public virtual float EvaluateFloat(float time) => Evaluate(FixedScalar.FromSingle(time)).ToSingle();

        public static TimelineRuntimeCurve Prepare(TimelineContentCurve curve, TimelineRuntimeNumericTarget target)
        {
            if (curve.Keys.Count == 0)
                throw new InvalidOperationException("Timeline motion requires a nonempty curve.");
            return target switch
            {
                TimelineRuntimeNumericTarget.Fixed => new FixedCurve(curve),
                TimelineRuntimeNumericTarget.Float32 => new FloatCurve(curve),
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            };
        }

        public static TimelineRuntimeCurve PrepareOptional(TimelineContentCurve curve, float emptyValue) =>
            curve.Keys.Count == 0 ? new ConstantCurve(emptyValue) : new FloatCurve(curve);

        sealed class ConstantCurve : TimelineRuntimeCurve
        {
            readonly float m_Value;
            public ConstantCurve(float value) => m_Value = value;
            public override FixedScalar Evaluate(FixedScalar time) => FixedScalar.FromSingle(m_Value);
            public override float EvaluateFloat(float time) => m_Value;
        }

        sealed class FixedCurve : TimelineRuntimeCurve
        {
            readonly FixedGameplayAbilityCurve m_Curve;
            public FixedCurve(TimelineContentCurve source)
            {
                var keys = new FixedGameplayAbilityCurveKey[source.Keys.Count];
                for (int i = 0; i < keys.Length; i++)
                {
                    TimelineContentCurveKey key = source.Keys[i];
                    keys[i] = new FixedGameplayAbilityCurveKey(
                        FixedScalar.FromSingle(key.Time), FixedScalar.FromSingle(key.Value),
                        float.IsInfinity(key.InTangent) ? FixedScalar.Zero : FixedScalar.FromSingle(key.InTangent),
                        float.IsInfinity(key.OutTangent) ? FixedScalar.Zero : FixedScalar.FromSingle(key.OutTangent),
                        FixedScalar.FromSingle(key.InWeight), FixedScalar.FromSingle(key.OutWeight), key.WeightedMode,
                        i + 1 < keys.Length && (float.IsInfinity(key.OutTangent) || float.IsInfinity(source.Keys[i + 1].InTangent)));
                }
                m_Curve = new FixedGameplayAbilityCurve(source.PreWrapMode, source.PostWrapMode, keys);
            }

            public override FixedScalar Evaluate(FixedScalar time)
            {
                FixedScalar first = m_Curve.Keys[0].Time;
                FixedScalar last = m_Curve.Keys[m_Curve.Keys.Count - 1].Time;
                if (first != last && (time < first || time > last))
                {
                    int wrap = time < first ? m_Curve.PreWrapMode : m_Curve.PostWrapMode;
                    if (wrap == 2 || wrap == 4)
                    {
                        long duration = (last - first).Raw;
                        long period = wrap == 4 ? checked(duration * 2) : duration;
                        long local = (time.Raw - first.Raw) % period;
                        if (local < 0) local += period;
                        if (wrap == 4 && local > duration) local = period - local;
                        time = FixedScalar.FromRaw(first.Raw + local);
                    }
                }
                return m_Curve.Evaluate(time, FixedScalar.Zero);
            }
        }

        sealed class FloatCurve : TimelineRuntimeCurve
        {
            readonly Float32GameplayAbilityCurve m_Curve;
            public FloatCurve(TimelineContentCurve source)
            {
                var keys = new Float32GameplayAbilityCurveKey[source.Keys.Count];
                for (int i = 0; i < keys.Length; i++)
                {
                    TimelineContentCurveKey key = source.Keys[i];
                    keys[i] = new Float32GameplayAbilityCurveKey(
                        Float32Scalar.FromDouble(key.Time), Float32Scalar.FromDouble(key.Value),
                        Float32Scalar.FromDouble(float.IsInfinity(key.InTangent) ? 0f : key.InTangent),
                        Float32Scalar.FromDouble(float.IsInfinity(key.OutTangent) ? 0f : key.OutTangent),
                        Float32Scalar.FromDouble(key.InWeight), Float32Scalar.FromDouble(key.OutWeight), key.WeightedMode,
                        i + 1 < keys.Length && (float.IsInfinity(key.OutTangent) || float.IsInfinity(source.Keys[i + 1].InTangent)));
                }
                m_Curve = new Float32GameplayAbilityCurve(source.PreWrapMode, source.PostWrapMode, keys);
            }

            public override FixedScalar Evaluate(FixedScalar time) => FixedScalar.FromSingle(EvaluateFloat(time.ToSingle()));
            public override float EvaluateFloat(float time)
            {
                float first = m_Curve.Keys[0].Time.ToSingle();
                float last = m_Curve.Keys[m_Curve.Keys.Count - 1].Time.ToSingle();
                if (first != last && (time < first || time > last))
                {
                    int wrap = time < first ? m_Curve.PreWrapMode : m_Curve.PostWrapMode;
                    if (wrap == 2 || wrap == 4)
                    {
                        float duration = last - first;
                        float period = wrap == 4 ? duration * 2f : duration;
                        float local = (time - first) % period;
                        if (local < 0f) local += period;
                        if (wrap == 4 && local > duration) local = period - local;
                        time = first + local;
                    }
                }
                return m_Curve.Evaluate(Float32Scalar.FromDouble(time), Float32Scalar.Zero).ToSingle();
            }
        }
    }

    public readonly struct TimelineRuntimeMotionPosition
    {
        public TimelineRuntimeMotionPosition(FixedScalar x, FixedScalar y, FixedScalar z, FixedScalar yaw)
        {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
        }

        public FixedScalar X { get; }
        public FixedScalar Y { get; }
        public FixedScalar Z { get; }
        public FixedScalar Yaw { get; }
    }

    internal sealed class TimelineRuntimeMotionCurve
    {
        readonly TimelineRuntimeIndexedClip m_Indexed;
        TimelineContentClip m_Clip => m_Indexed.Clip;
        FixedScalar m_SourceStart;
        FixedScalar m_SourceEnd;
        readonly TimelineRuntimeCurve m_X;
        readonly TimelineRuntimeCurve m_Y;
        readonly TimelineRuntimeCurve m_Z;
        readonly TimelineRuntimeCurve m_Yaw;
        readonly TimelineRuntimeCurve m_Weight;
        readonly TimelineRuntimeCurve m_EaseIn;
        readonly TimelineRuntimeCurve m_EaseOut;

        public TimelineRuntimeMotionCurve(TimelineRuntimeIndexedClip indexed, TimelineRuntimeNumericTarget target)
        {
            m_Indexed = indexed;
            TimelineContentClip clip = indexed.Clip;
            m_SourceStart = FixedScalar.FromSingle(clip.TimeRange.SourceStart);
            m_SourceEnd = FixedScalar.FromSingle(clip.TimeRange.SourceEnd);
            var motion = (TimelineContentMotion)clip.Parameters;
            m_X = TimelineRuntimeCurve.Prepare(motion.X, target);
            m_Y = TimelineRuntimeCurve.Prepare(motion.Y, target);
            m_Z = TimelineRuntimeCurve.Prepare(motion.Z, target);
            m_Yaw = TimelineRuntimeCurve.Prepare(motion.Yaw, target);
            m_Weight = TimelineRuntimeCurve.Prepare(clip.RequireCurve("motion.weight"), target);
            m_EaseIn = TimelineRuntimeCurve.Prepare(clip.RequireCurve("motion.ease-in"), target);
            m_EaseOut = TimelineRuntimeCurve.Prepare(clip.RequireCurve("motion.ease-out"), target);
        }

        public TimelineRuntimeMotionPosition Evaluate(FixedScalar timelineTime)
        {
            FixedScalar sourceTime = FixedScalar.Clamp(
                m_SourceStart + FixedScalar.Clamp(timelineTime - m_Clip.StartTime, FixedScalar.Zero, m_Clip.DurationTime),
                m_SourceStart, m_SourceEnd);
            return new TimelineRuntimeMotionPosition(
                m_X.Evaluate(sourceTime), m_Y.Evaluate(sourceTime), m_Z.Evaluate(sourceTime), m_Yaw.Evaluate(sourceTime));
        }

        internal void RefreshTimeRange()
        {
            m_SourceStart = FixedScalar.FromSingle(m_Clip.TimeRange.SourceStart);
            m_SourceEnd = FixedScalar.FromSingle(m_Clip.TimeRange.SourceEnd);
        }

        internal TimelineRuntimeMotionCurve(TimelineRuntimeIndexedClip indexed, TimelineRuntimeMotionCurve source)
        {
            m_Indexed = indexed;
            TimelineContentClip clip = indexed.Clip;
            m_X = source.m_X;
            m_Y = source.m_Y;
            m_Z = source.m_Z;
            m_Yaw = source.m_Yaw;
            m_Weight = source.m_Weight;
            m_EaseIn = source.m_EaseIn;
            m_EaseOut = source.m_EaseOut;
            RefreshTimeRange();
        }

        public bool TrySample(
            FixedScalar previousTime, FixedScalar currentTime, string sourceId, string sourceName,
            out TimelineMotionCurveContribution contribution)
        {
            contribution = default;
            if (currentTime < m_Clip.StartTime || previousTime >= m_Clip.EndTime)
                return false;
            FixedScalar local = FixedScalar.Clamp(currentTime - m_Clip.StartTime, FixedScalar.Zero, m_Clip.DurationTime);
            FixedScalar normalized = local / m_Clip.DurationTime;
            FixedScalar weight = m_Weight.Evaluate(normalized);
            if (m_Clip.EaseInTime > FixedScalar.Zero && local < m_Clip.EaseInTime)
                weight *= m_EaseIn.Evaluate(local / m_Clip.EaseInTime);
            FixedScalar remaining = FixedScalar.Max(FixedScalar.Zero, m_Clip.EndTime - currentTime);
            if (m_Clip.EaseOutTime > FixedScalar.Zero && remaining < m_Clip.EaseOutTime)
                weight *= FixedScalar.One - m_EaseOut.Evaluate(FixedScalar.One - remaining / m_Clip.EaseOutTime);
            weight = FixedScalar.Clamp(weight, FixedScalar.Zero, FixedScalar.One);
            if (weight == FixedScalar.Zero)
                return false;
            TimelineRuntimeMotionPosition previous = Evaluate(previousTime);
            TimelineRuntimeMotionPosition current = Evaluate(currentTime);
            var motion = (TimelineContentMotion)m_Clip.Parameters;
            contribution = new TimelineMotionCurveContribution(
                sourceId, sourceName, m_Indexed.Track.Name, motion.CurveId, m_Clip.AuthoringId,
                motion.Space, motion.Channel, motion.BlendMode,
                current.X - previous.X, current.Y - previous.Y, current.Z - previous.Z,
                current.Yaw - previous.Yaw, motion.Priority, weight, motion.ConsumeLowerChannels, normalized);
            return contribution.CanResolve;
        }
    }


    internal sealed class TimelineRuntimeMotionSampling
    {
        readonly Dictionary<string, TimelineRuntimeMotionCurve> m_Curves = new(StringComparer.Ordinal);
        readonly TimelineRuntimeMotionCurve[] m_IndexedCurves;

        public TimelineRuntimeMotionSampling(TimelineRuntimeClipIndex motions, TimelineRuntimeNumericTarget target)
        {
            m_IndexedCurves = new TimelineRuntimeMotionCurve[motions.Count];
            for (int index = 0; index < motions.Count; index++)
            {
                var clip = motions[index].Clip;
                var curve = new TimelineRuntimeMotionCurve(motions[index], target);
                m_Curves.Add(clip.AuthoringId, curve);
                m_IndexedCurves[index] = curve;
            }
        }

        internal TimelineRuntimeMotionSampling(TimelineRuntimeClipIndex motions, TimelineRuntimeMotionSampling source)
        {
            m_IndexedCurves = new TimelineRuntimeMotionCurve[motions.Count];
            for (int index = 0; index < motions.Count; index++)
            {
                var clip = motions[index].Clip;
                var curve = new TimelineRuntimeMotionCurve(motions[index], source.m_Curves[clip.AuthoringId]);
                m_Curves.Add(clip.AuthoringId, curve);
                m_IndexedCurves[index] = curve;
            }
        }

        internal void RefreshTimeRanges()
        {
            for (int i = 0; i < m_IndexedCurves.Length; i++)
                m_IndexedCurves[i].RefreshTimeRange();
        }


        public TimelineRuntimeMotionPosition SampleSource(string clipId, FixedScalar time) => m_Curves[clipId].Evaluate(time);

        public void Sample(
            TimelineContentUnit timeline, TimelineRuntimeClipIndex motions, TimelineRuntimeEvaluationSegments segments,
            List<int> selected,
            TimelineRuntimeSampleBuffer<TimelineMotionCurveContribution> contributions)
        {
            selected.Clear();
            for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
            {
                TimelineRuntimeEvaluationSegment segment = segments[segmentIndex];
                motions.Select(segment.PreviousTime, segment.CurrentTime, false, selected);
            }
            for (int selectedIndex = 0; selectedIndex < selected.Count; selectedIndex++)
            {
                int index = selected[selectedIndex];
                var clip = motions[index].Clip;
                TimelineRuntimeMotionCurve curve = m_IndexedCurves[index];
                TimelineMotionCurveContribution last = default;
                FixedScalar x = FixedScalar.Zero;
                FixedScalar y = FixedScalar.Zero;
                FixedScalar z = FixedScalar.Zero;
                FixedScalar yaw = FixedScalar.Zero;
                bool sampled = false;
                for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
                {
                    TimelineRuntimeEvaluationSegment segment = segments[segmentIndex];
                    if (!curve.TrySample(segment.PreviousTime, segment.CurrentTime,
                            timeline.Identity, timeline.Name, out var contribution))
                        continue;
                    if (segments.Count == 1 || ((TimelineContentMotion)clip.Parameters).BlendMode != TimelineMotionBlendMode.Override)
                    {
                        contributions.Add(contribution);
                        continue;
                    }
                    last = contribution;
                    sampled = true;
                    x += contribution.DisplacementX * contribution.Weight;
                    y += contribution.DisplacementY * contribution.Weight;
                    z += contribution.DisplacementZ * contribution.Weight;
                    yaw += contribution.YawDegrees * contribution.Weight;
                }
                if (sampled)
                {
                    // Sequential slices of one source must enter Override arbitration as one contribution.
                    contributions.Add(new TimelineMotionCurveContribution(
                        last.SourceId, last.SourceName, last.TrackName, last.CurveId, last.ClipAuthoringId,
                        last.Space, last.Channel, last.BlendMode, x, y, z, yaw,
                        last.Priority, FixedScalar.One, last.ConsumeLowerChannels, last.NormalizedTime));
                }
            }
        }

    }
}
