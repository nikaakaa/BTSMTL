using System;
using System.Collections.Generic;
using BTSMTL.Orchestration;
using ThirdPersonSimulation;

namespace BTSMTL.Timeline.Runtime
{
    public interface ITimelineRuntimeSourceTimeMapping<TScalar> where TScalar : unmanaged
    {
        TScalar MapSourceTime(in TimelineContentClip clip, int clipIndex, OrchestrationTime time);
    }

    public sealed class TimelineRuntimeClipSampling<TScalar, TMath, TLowering, TProjection> :
        ITimelineRuntimeSourceTimeMapping<TScalar>
        where TScalar : unmanaged, IComparable<TScalar>, IEquatable<TScalar>
        where TMath : struct, IScalarMath<TScalar>
        where TLowering : struct, IScalarContentLowering<TScalar>
        where TProjection : struct, IOrchestrationTimelineTimeProjection<OrchestrationTime, TScalar>
    {
        readonly struct WeightCurves
        {
            public WeightCurves(TimelineRuntimeCurve<TScalar> weight, TimelineRuntimeCurve<TScalar> easeIn,
                TimelineRuntimeCurve<TScalar> easeOut, TScalar normalizationDurationMinimum,
                TScalar sourceStart, TScalar sourceEnd)
            {
                Weight = weight;
                EaseIn = easeIn;
                EaseOut = easeOut;
                NormalizationDurationMinimum = normalizationDurationMinimum;
                SourceStart = sourceStart;
                SourceEnd = sourceEnd;
            }

            public readonly TimelineRuntimeCurve<TScalar> Weight;
            public readonly TimelineRuntimeCurve<TScalar> EaseIn;
            public readonly TimelineRuntimeCurve<TScalar> EaseOut;
            public readonly TScalar NormalizationDurationMinimum;
            public readonly TScalar SourceStart;
            public readonly TScalar SourceEnd;
        }

        readonly WeightCurves[] m_Weights;
        readonly GameplayAbilityCurveKey<TScalar>[] m_CurveKeys;
        readonly int[] m_KindIndices;
        readonly string[] m_Kinds;
        readonly Dictionary<string, int> m_KindsByName;
        readonly TimelineExecutionDomain m_Domain;

        public TimelineRuntimeClipSampling(TimelineContentUnit content, TimelineExecutionDomain domain)
        {
            TMath math = default;
            TLowering lowering = default;
            m_Domain = domain;
            m_Weights = new WeightCurves[content.Clips.Count];
            m_KindIndices = new int[content.Clips.Count];
            m_KindsByName = new Dictionary<string, int>(StringComparer.Ordinal);
            var kinds = new List<string>();
            var curves = new TimelineRuntimeCurveBuilder<TScalar, TLowering>();
            for (int index = 0; index < content.Clips.Count; index++)
            {
                TimelineContentClip clip = content.Clips[index];
                if (clip.ContentKind != TimelineContentKind.Sampled)
                    continue;
                if (!m_KindsByName.TryGetValue(clip.ContractKind, out int kindIndex))
                {
                    kindIndex = kinds.Count;
                    kinds.Add(clip.ContractKind);
                    m_KindsByName.Add(clip.ContractKind, kindIndex);
                }
                m_KindIndices[index] = kindIndex;
                if (clip.TrackMuted || clip.ExecutionPolicy.Domain != domain)
                    continue;
                TimelineContentSampling sampling = clip.Sampling;
                m_Weights[index] = new WeightCurves(
                    PrepareCurve(curves, in clip, sampling.WeightCurveIndex, math.One),
                    PrepareCurve(curves, in clip, sampling.EaseInCurveIndex, math.One),
                    PrepareCurve(curves, in clip, sampling.EaseOutCurveIndex, math.Zero),
                    lowering.FromSingle(sampling.NormalizationDurationMinimum),
                    lowering.FromSingle(clip.TimeRange.SourceStart), lowering.FromSingle(clip.TimeRange.SourceEnd));
            }
            m_CurveKeys = curves.ToKeyArray();
            m_Kinds = kinds.ToArray();
        }

        internal TimelineRuntimeClipSampling(TimelineRuntimeClipSampling<TScalar, TMath, TLowering, TProjection> source,
            TimelineContentUnit content)
        {
            m_Domain = source.m_Domain;
            m_Weights = (WeightCurves[])source.m_Weights.Clone();
            m_CurveKeys = source.m_CurveKeys;
            m_KindIndices = source.m_KindIndices;
            m_Kinds = source.m_Kinds;
            m_KindsByName = source.m_KindsByName;
            RefreshTimeRanges(content);
        }

        internal void RefreshTimeRanges(TimelineContentUnit content)
        {
            TLowering lowering = default;
            for (int index = 0; index < content.Clips.Count; index++)
            {
                TimelineContentClip clip = content.Clips[index];
                if (clip.ContentKind != TimelineContentKind.Sampled || clip.TrackMuted ||
                    clip.ExecutionPolicy.Domain != m_Domain)
                    continue;
                WeightCurves curves = m_Weights[index];
                m_Weights[index] = new WeightCurves(curves.Weight, curves.EaseIn, curves.EaseOut,
                    curves.NormalizationDurationMinimum, lowering.FromSingle(clip.TimeRange.SourceStart),
                    lowering.FromSingle(clip.TimeRange.SourceEnd));
            }
        }

        static TimelineRuntimeCurve<TScalar> PrepareCurve(TimelineRuntimeCurveBuilder<TScalar, TLowering> curves,
            in TimelineContentClip clip, int curveIndex, TScalar emptyValue) =>
            curveIndex == -1
                ? curves.Constant(emptyValue)
                : curves.PrepareOptional(clip.Curves[curveIndex], emptyValue);

        public int SampleKindCount => m_Kinds.Length;
        public string SampleKindAt(int kindIndex) => m_Kinds[kindIndex];
        public int RequireSampleKind(string contractKind) => m_KindsByName[contractKind];
        internal int KindIndexAt(int clipIndex) => m_KindIndices[clipIndex];

        public TScalar MapSourceTime(in TimelineContentClip clip, int clipIndex, OrchestrationTime time)
        {
            TMath math = default;
            TProjection projection = default;
            WeightCurves curves = m_Weights[clipIndex];
            TScalar local = projection.Project(OrchestrationTime.Clamp(time - clip.StartTime,
                OrchestrationTime.Zero, clip.DurationTime));
            TScalar duration = math.Max(curves.NormalizationDurationMinimum, projection.Project(clip.DurationTime));
            TScalar normalized = math.Clamp(math.Divide(local, duration), math.Zero, math.One);
            return SourceTime(in curves, in clip, local, normalized);
        }

        public bool TrySample(in TimelineContentClip clip, int clipIndex, OrchestrationTime previousTime, OrchestrationTime time,
            int cycle, TimelinePresentationSampleReason reason, out TimelineRuntimeSample<TScalar> sample)
        {
            TMath math = default;
            TProjection projection = default;
            WeightCurves curves = m_Weights[clipIndex];
            bool hold = time > clip.EndTime && clip.Sampling.Extrapolation == ExtraPolationMode.Hold;
            OrchestrationTime localTime = hold ? clip.DurationTime
                : OrchestrationTime.Clamp(time - clip.StartTime, OrchestrationTime.Zero, clip.DurationTime);
            TScalar local = projection.Project(localTime);
            TScalar remaining = projection.Project(OrchestrationTime.Max(OrchestrationTime.Zero, clip.EndTime - time));
            TScalar duration = math.Max(curves.NormalizationDurationMinimum, projection.Project(clip.DurationTime));
            TScalar normalized = math.Clamp(math.Divide(local, duration), math.Zero, math.One);
            TScalar weight = Weight(in curves, m_CurveKeys, in clip, local, remaining, normalized, hold);
            if (!clip.Sampling.EmitZeroWeight && math.LessThanOrEqual(weight, math.Zero))
            {
                sample = default;
                return false;
            }
            TScalar sourceTime = SourceTime(in curves, in clip, local, normalized);
            OrchestrationTime previousLocalTime = OrchestrationTime.Clamp(previousTime - clip.StartTime,
                OrchestrationTime.Zero, clip.DurationTime);
            TScalar previousLocal = projection.Project(previousLocalTime);
            TScalar previousNormalized = math.Clamp(math.Divide(previousLocal, duration), math.Zero, math.One);
            TScalar previousSourceTime = SourceTime(in curves, in clip, previousLocal, previousNormalized);
            sample = new TimelineRuntimeSample<TScalar>(clipIndex, m_KindIndices[clipIndex],
                weight, previousSourceTime, sourceTime, normalized, cycle, reason);
            return true;
        }

        static TScalar SourceTime(in WeightCurves curves, in TimelineContentClip clip, TScalar local, TScalar normalized)
        {
            TMath math = default;
            TProjection projection = default;
            if (clip.Sampling.SourceMapping == TimelineSampleSourceMapping.ClipSeconds)
                return math.Add(local, projection.Project(clip.ClipInTime));
            TScalar sourceStart = curves.SourceStart;
            TScalar sourceEnd = curves.SourceEnd;
            return clip.Sampling.SourceMapping == TimelineSampleSourceMapping.SourceRange
                ? math.Add(math.Add(sourceStart, math.Multiply(normalized, math.Subtract(sourceEnd, sourceStart))),
                    projection.Project(clip.ClipInTime))
                : math.Clamp(math.Add(sourceStart, local), sourceStart, sourceEnd);
        }

        static TScalar Weight(in WeightCurves curves, ReadOnlySpan<GameplayAbilityCurveKey<TScalar>> keys,
            in TimelineContentClip clip, TScalar local, TScalar remaining, TScalar normalized, bool hold)
        {
            TMath math = default;
            TProjection projection = default;
            TimelineRuntimeCurveEvaluator<TScalar, TMath> evaluator = default;
            TScalar fadeIn = math.One;
            TScalar fadeOut = math.One;
            TScalar easeIn = projection.Project(clip.EaseInTime);
            TScalar easeOut = projection.Project(clip.EaseOutTime);
            if (!hold && math.GreaterThan(easeIn, math.Zero) && math.GreaterThan(easeIn, local))
                fadeIn = evaluator.Evaluate(in curves.EaseIn, keys,
                    math.Clamp(math.Divide(local, easeIn), math.Zero, math.One));
            if (!hold && math.GreaterThan(easeOut, math.Zero) && math.GreaterThan(easeOut, remaining))
                fadeOut = math.Subtract(math.One, evaluator.Evaluate(in curves.EaseOut, keys, math.Clamp(
                    math.Subtract(math.One, math.Divide(remaining, easeOut)), math.Zero, math.One)));
            return math.Clamp(math.Multiply(math.Multiply(evaluator.Evaluate(in curves.Weight, keys, normalized),
                fadeIn), fadeOut), math.Zero, math.One);
        }
    }

    readonly struct TimelineRuntimeFloat32TimeProjection
        : IOrchestrationTimelineTimeProjection<OrchestrationTime, Float32Scalar>
    {
        public Float32Scalar Project(OrchestrationTime time) => Float32Scalar.FromSingle(time.AsSingle);
    }
}
