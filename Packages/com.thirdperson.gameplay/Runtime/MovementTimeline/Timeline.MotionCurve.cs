using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using BTSMTL.Timeline;
using BTSMTL.Timeline.Runtime;
using ThirdPersonSimulation;
#if UNITY_5_3_OR_NEWER
using BTSMTL;
#endif

namespace ThirdPersonGameplay.Movement.Timeline
{
#if UNITY_5_3_OR_NEWER
    [ScriptGuid("6f2a51d8c9b34d5f8a0e7b4c2d9f136a"), Ordered(1.5f), Color(126, 220, 146)]
#endif
    public sealed partial class MotionCurveTrack : SampledTrack
    {
    }

#if UNITY_5_3_OR_NEWER
    [ScriptGuid("6f2a51d8c9b34d5f8a0e7b4c2d9f136a"), Color(126, 220, 146)]
#endif
    public sealed partial class MotionCurveClip : SampledClip, ITimelineClipTimeRangeValidator
#if UNITY_EDITOR || BTSMTL_AUTHORING
        , ITimelineAuthoringReferenceCurveSource,
        ITimelineAuthoringClipSplitSource, ITimelineAuthoringSourceTimeEditing
#endif
    {
        static readonly TimelineCurve s_ZeroCurve = TimelineCurveChannelCatalog.Linear(0f, 0f, 1f, 0f);

        public float SourceDuration => Math.Max(0f, SourceEndTime - SourceStartTime);

        public bool TryValidateTimeRange(in TimelineClipTimeRange range, out string error)
        {
            if (!float.IsFinite(range.SourceStart) || !float.IsFinite(range.SourceEnd) ||
                range.SourceStart < 0f || range.SourceEnd <= range.SourceStart ||
                range.SourceEnd > SourceContent.Duration ||
                OrchestrationTime.FromSeconds(range.SourceEnd - range.SourceStart) > range.End - range.Start)
            {
                error = $"Motion Clip '{Name}' 的源区间必须位于素材内，并且不能长于时间条。";
                return false;
            }
            error = string.Empty;
            return true;
        }

        public OrchestrationTime CurveEndTime => StartTime + OrchestrationTime.FromSeconds(SourceDuration);
        public TimelineCurve SourcePositionX => SourceContent.EvaluationMode == RootMotionCurveEvaluationMode.ForwardDistanceYaw
            ? s_ZeroCurve
            : SourceContent.LocalPositionX;

        public TimelineCurve SourcePositionY => SourceContent.EvaluationMode == RootMotionCurveEvaluationMode.ForwardDistanceYaw
            ? s_ZeroCurve
            : SourceContent.LocalPositionY;

        public TimelineCurve SourcePositionZ => SourceContent.EvaluationMode == RootMotionCurveEvaluationMode.ForwardDistanceYaw
            ? SourceContent.ForwardDistance
            : SourceContent.LocalPositionZ;

        public TimelineCurve SourceYaw => SourceContent.LocalYaw;

#if UNITY_EDITOR || BTSMTL_AUTHORING
        public bool CanSplit => true;

        public IReadOnlyList<TimelineReferenceCurveDescriptor> ReferenceCurves => new[]
        {
            ReferenceCurve("source.position-x", "Position X", SourcePositionX),
            ReferenceCurve("source.position-y", "Position Y", SourcePositionY),
            ReferenceCurve("source.position-z", "Position Z", SourcePositionZ),
            ReferenceCurve("source.yaw", "Yaw", SourceYaw)
        };

        TimelineReferenceCurveDescriptor ReferenceCurve(string channelId, string displayName, TimelineCurve source) =>
            new TimelineReferenceCurveDescriptor(channelId, displayName, SourceContent.DisplayName,
                (target, sourceStart, sourceEnd, duration) =>
                    SourceDisplayCurve(source, sourceStart, sourceEnd, duration));

        public void SplitSource(Clip copy, OrchestrationTime splitTime)
        {
            var motionCopy = (MotionCurveClip)copy;
            float splitSourceTime = SourceStartTime + (splitTime - StartTime).AsSingle;
            float originalSourceEnd = SourceEndTime;
            ConfigureSource(SourceCurve, SourceStartTime, splitSourceTime);
            motionCopy.ConfigureSource(motionCopy.SourceCurve, splitSourceTime, originalSourceEnd);
        }

        public void AdjustSourceRange(in TimelineClipTimeRange original, double originalStart,
            double currentStart, double currentEnd, bool trimStart,
            out float sourceStart, out float sourceEnd)
        {
            sourceStart = original.SourceStart;
            double duration = Math.Max(0d, currentEnd - currentStart);
            if (trimStart)
                sourceStart = (float)Math.Max(0d,
                    Math.Min(original.SourceEnd, original.SourceStart + currentStart - originalStart));
            sourceEnd = (float)Math.Min(SourceContent.Duration, sourceStart + duration);
        }

#endif

        public Float32Vector3 EvaluatePositionAtTimelineTime(OrchestrationTime timelineTime)
        {
            return SourceContent.EvaluatePosition(SourceTime(timelineTime));
        }

        public float EvaluateYawAtTimelineTime(OrchestrationTime timelineTime)
        {
            return SourceContent.EvaluateYaw(SourceTime(timelineTime));
        }

        float SourceTime(OrchestrationTime timelineTime) => Math.Min(SourceEndTime,
            Math.Max(SourceStartTime, SourceStartTime +
                OrchestrationTime.Max(OrchestrationTime.Zero, timelineTime - StartTime).AsSingle));

#if UNITY_EDITOR || BTSMTL_AUTHORING
        static TimelineCurve SourceDisplayCurve(TimelineCurve source,
            float sourceStartTime, float sourceEndTime, float timelineDuration)
        {
            var result = new List<TimelineCurveKey>();
            var builder = new TimelineRuntimeCurveBuilder<Float32Scalar, Float32ScalarContentLowering>();
            TimelineRuntimeCurve<Float32Scalar> compiled = builder.PrepareOptional(
                TimelineCurveContent.CaptureSourceCurve("motion.reference", source), Float32Scalar.Zero);
            GameplayAbilityCurveKey<Float32Scalar>[] compiledKeys = builder.ToKeyArray();
            var evaluator = new TimelineRuntimeCurveEvaluator<Float32Scalar, Float32SimulationMath>();
            float Evaluate(float time) => evaluator.Evaluate(in compiled, compiledKeys, Float32Scalar.FromSingle(time)).ToSingle();
            float sourceDuration = sourceEndTime - sourceStartTime;
            bool hasStartKey = false;
            bool hasEndKey = false;
            int firstKeyAfterStart = -1;
            int lastKeyBeforeEnd = -1;
            for (int index = 0; index < source.Keys.Count; index++)
            {
                TimelineCurveKey key = source.Keys[index];
                if (Approximately(key.Time, sourceStartTime))
                {
                    result.Add(AtTime(key, 0f));
                    hasStartKey = true;
                    continue;
                }
                if (Approximately(key.Time, sourceEndTime))
                {
                    result.Add(AtTime(key, sourceDuration));
                    hasEndKey = true;
                    continue;
                }
                if (key.Time > sourceStartTime && firstKeyAfterStart < 0)
                    firstKeyAfterStart = index;
                if (key.Time < sourceEndTime)
                    lastKeyBeforeEnd = index;
                if (key.Time <= sourceStartTime || key.Time >= sourceEndTime)
                    continue;
                result.Add(AtTime(key, key.Time - sourceStartTime));
            }
            if (!hasStartKey)
            {
                float tangent = firstKeyAfterStart >= 0 ? source.Keys[firstKeyAfterStart].InTangent : 0f;
                result.Add(new TimelineCurveKey(0f, Evaluate(sourceStartTime), tangent, tangent, 0f, 0f, 0, 0));
            }
            if (!hasEndKey)
            {
                float tangent = lastKeyBeforeEnd >= 0 ? source.Keys[lastKeyBeforeEnd].OutTangent : 0f;
                result.Add(new TimelineCurveKey(sourceDuration, Evaluate(sourceEndTime), tangent, tangent, 0f, 0f, 0, 0));
            }
            if (timelineDuration > sourceDuration)
                result.Add(new TimelineCurveKey(timelineDuration, Evaluate(sourceEndTime), 0f, 0f, 0f, 0f, 0, 0));
            result.Sort((left, right) => left.Time.CompareTo(right.Time));
            return new TimelineCurve(8, 8, result);
        }

        static TimelineCurveKey AtTime(TimelineCurveKey key, float time) => new TimelineCurveKey(time, key.Value,
            key.InTangent, key.OutTangent, key.InWeight, key.OutWeight, key.WeightedMode, key.TangentMode);

        static bool Approximately(float left, float right) => Math.Abs(left - right) <
            Math.Max(0.000001f * Math.Max(Math.Abs(left), Math.Abs(right)), float.Epsilon * 8f);
#endif

        public override void Init(Track track)
        {
            base.Init(track);
            TimelineClipTimeRange range = CaptureTimeRange();
            if (!TryValidateTimeRange(in range, out string error))
                throw new InvalidOperationException(error);
        }

#if UNITY_EDITOR || BTSMTL_AUTHORING
        public override void ConfigureAuthoringReference(string reference)
        {
            m_SourceCurve = reference;
        }
#endif
    }

}
