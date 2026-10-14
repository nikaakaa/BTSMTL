using System;
using Photon.Deterministic;

namespace BTSMTL.Timeline.Runtime
{
    sealed class TimelineRuntimeClipSampling
    {
        sealed class WeightCurves
        {
            internal TimelineRuntimeCurve Weight;
            internal TimelineRuntimeCurve EaseIn;
            internal TimelineRuntimeCurve EaseOut;
        }

        readonly WeightCurves[] m_Weights;
        readonly TimelineRuntimeCurve[] m_Values;

        public TimelineRuntimeClipSampling(TimelineContentUnit content)
        {
            m_Weights = new WeightCurves[content.Clips.Count];
            m_Values = new TimelineRuntimeCurve[content.Clips.Count];
            for (int i = 0; i < content.Clips.Count; i++)
            {
                TimelineContentClip clip = content.Clips[i];
                string prefix = clip.Parameters switch
                {
                    TimelineContentAnimation => "animation",
                    TimelineContentCameraState => "camera-state",
                    TimelineContentCameraResponse => "camera-response",
                    TimelineContentCameraEffect => "camera-effect",
                    _ => null
                };
                if (prefix != null)
                    m_Weights[i] = new WeightCurves
                    {
                        Weight = TimelineRuntimeCurve.PrepareOptional(clip.RequireCurve(prefix + ".weight"), 1f),
                        EaseIn = TimelineRuntimeCurve.PrepareOptional(clip.RequireCurve(prefix + ".ease-in"), 1f),
                        EaseOut = TimelineRuntimeCurve.PrepareOptional(clip.RequireCurve(prefix + ".ease-out"), 0f)
                    };
                if (clip.Parameters is TimelineContentSceneParameter)
                    m_Values[i] = TimelineRuntimeCurve.PrepareOptional(clip.RequireCurve("scene-presentation.value"), 0f);
            }
        }

        public void SampleAnimation(TimelineRuntimeIndexedClip indexed, TimelineContentUnit content,
            FP time, bool loop, int cycle, TimelineRuntimeSampleBuffer<TimelineAnimationContribution> output)
        {
            TimelineContentClip clip = indexed.Clip;
            var animation = (TimelineContentAnimation)clip.Parameters;
            bool hold = time > clip.EndTime && animation.Extrapolation == ExtraPolationMode.Hold;
            float local = hold ? clip.DurationTime.AsFloat : FPMath.Clamp(time - clip.StartTime,
                FP._0, clip.DurationTime).AsFloat;
            float remaining = FPMath.Max(FP._0, clip.EndTime - time).AsFloat;
            float normalized = Math.Clamp(local / Math.Max(0.0001f, clip.DurationTime.AsFloat), 0f, 1f);
            float weight = Weight(indexed.ContentIndex, in clip, local, remaining, normalized, hold);
            if (weight <= 0f)
                return;
            output.Add(new TimelineAnimationContribution(indexed.TrackIndex, indexed.ClipIndex,
                content.AuthoringId, clip.TrackAuthoringId, clip.AuthoringId, content.Identity, content.Name,
                indexed.Track.Name, animation.ResourceId, animation.ChannelId, animation.SlotId,
                animation.BlendProfileId, local + clip.ClipInTime.AsFloat, normalized, weight,
                loop, clip.ClipInTime.AsFloat, clip.DurationTime.AsFloat, cycle));
        }

        public void SampleCameraAndScene(TimelineRuntimeIndexedClip indexed, TimelineContentUnit content,
            FP time, int cycle, TimelineExecutionIdentity execution, ulong generation,
            TimelineRuntimeSampleBuffer<TimelineCameraStateSample> states,
            TimelineRuntimeSampleBuffer<TimelineCameraResponseSample> responses,
            TimelineRuntimeSampleBuffer<TimelineCameraResourceSample> resources,
            TimelineRuntimeSampleBuffer<TimelineRuntimeScenePresentationSample> scene)
        {
            TimelineContentClip clip = indexed.Clip;
            float local = FPMath.Clamp(time - clip.StartTime, FP._0, clip.DurationTime).AsFloat;
            float remaining = FPMath.Max(FP._0, clip.EndTime - time).AsFloat;
            float duration = clip.Parameters is TimelineContentCameraEffect { Kind: TimelineCameraResourceKind.Shot }
                ? clip.DurationTime.AsFloat : Math.Max(0.0001f, clip.DurationTime.AsFloat);
            float normalized = Math.Clamp(local / duration, 0f, 1f);
            if (clip.Parameters is TimelineContentSceneParameter parameter)
            {
                scene.Add(new TimelineRuntimeScenePresentationSample(clip.AuthoringId,
                    parameter.TargetBindingId, parameter.ParameterBindingId, parameter.ValueKind,
                    m_Values[indexed.ContentIndex].EvaluateFloat(normalized), normalized, time, cycle, execution, generation));
                return;
            }
            float weight = Weight(indexed.ContentIndex, in clip, local, remaining, normalized, false);
            if (weight <= 0f && clip.Parameters is not TimelineContentCameraEffect { Kind: TimelineCameraResourceKind.Shot })
                return;
            switch (clip.Parameters)
            {
                case TimelineContentCameraState state:
                    states.Add(new TimelineCameraStateSample(content.Identity, content.Name, indexed.Track.Name,
                        clip.TrackAuthoringId, clip.AuthoringId, state.SequenceId, state.Mode, state.Priority,
                        weight, state.BlendInSeconds, state.BlendOutSeconds, state.TargetKey, state.InterruptPolicy));
                    break;
                case TimelineContentCameraResponse response:
                    responses.Add(new TimelineCameraResponseSample(content.Identity, content.Name, indexed.Track.Name,
                        clip.TrackAuthoringId, clip.AuthoringId, response.LookResponse, response.ManualOrbitWeight,
                        response.PitchResponseWeight, response.YawResponseWeight, response.Priority, weight));
                    break;
                case TimelineContentCameraEffect effect:
                    resources.Add(new TimelineCameraResourceSample(content.Identity, content.Name,
                        clip.TrackAuthoringId, clip.AuthoringId, effect.Kind, effect.ResourceId, effect.Priority, weight, normalized,
                        effect.SourceStartTime + normalized * (effect.SourceEndTime - effect.SourceStartTime), effect.ResourceRevision,
                        cycle, effect.SourceStartTime, effect.SourceEndTime, time >= clip.EndTime));
                    break;
            }
        }

        float Weight(int index, in TimelineContentClip clip, float local, float remaining, float normalized, bool hold)
        {
            WeightCurves curves = m_Weights[index];
            float fadeIn = 1f;
            float fadeOut = 1f;
            float easeIn = clip.EaseInTime.AsFloat;
            float easeOut = clip.EaseOutTime.AsFloat;
            if (!hold && easeIn > 0f && local < easeIn)
                fadeIn = curves.EaseIn.EvaluateFloat(Math.Clamp(local / easeIn, 0f, 1f));
            if (!hold && easeOut > 0f && remaining < easeOut)
                fadeOut = 1f - curves.EaseOut.EvaluateFloat(Math.Clamp(1f - remaining / easeOut, 0f, 1f));
            return Math.Clamp(curves.Weight.EvaluateFloat(normalized) * fadeIn * fadeOut, 0f, 1f);
        }
    }
}
