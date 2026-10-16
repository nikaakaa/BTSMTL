using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public readonly struct ScenePresentationParameterId : IEquatable<ScenePresentationParameterId>
    {
        public ScenePresentationParameterId(string value)
        {
            Value = string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("Scene presentation parameter identity is required.", nameof(value))
                : value.Trim();
        }

        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public bool Equals(ScenePresentationParameterId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ScenePresentationParameterId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value ?? string.Empty;
    }

    public interface IScenePresentationParameterSink : ITimelineScenePresentationSink
    {
    }

    [TrackGroup("Base"), ScriptGuid("9a9b5b4c1d2e4f6a8b7c0d1e2f3a4b5c"), Ordered(7), Color(132, 224, 184)]
    public sealed class ScenePresentationParameterTrack : SampledTrack
    {
        public override string ContractKind => TimelineContractKinds.ScenePresentationParameterTrack;

#if UNITY_EDITOR
        public override Type ClipType => typeof(ScenePresentationParameterCurveClip);
#endif
    }

    [ScriptGuid("9a9b5b4c1d2e4f6a8b7c0d1e2f3a4b5c"), Color(132, 224, 184)]
#if UNITY_EDITOR
    [TimelineAuthoringProperty("targetBindingId", TimelineAuthoringPropertyKind.Text, MemberName = "m_TargetBindingId", Trimmed = true,
        BindingValueKinds = new[] { TimelineBindingValueKind.Target }, BindingAccess = TimelineBindingAccess.Input,
        BindingLifetime = TimelineBindingLifetime.Call)]
    [TimelineAuthoringProperty("parameterBindingId", TimelineAuthoringPropertyKind.Text, MemberName = "m_ParameterBindingId", Trimmed = true,
        BindingValueKinds = new[] { TimelineBindingValueKind.Scalar, TimelineBindingValueKind.Boolean },
        BindingAccess = TimelineBindingAccess.Write, BindingLifetime = TimelineBindingLifetime.Tick)]
    [TimelineAuthoringProperty("valueCurve", TimelineAuthoringPropertyKind.Object, MemberName = "m_ValueCurve")]
#endif
    public sealed class ScenePresentationParameterCurveClip : SampledClip, ITimelineExternalBindingUseSource
    {
        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
        string m_TargetBindingId = "target";

        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
        string m_ParameterBindingId = "openAmount";

        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
        AnimationCurve m_ValueCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public override string ContractKind => TimelineContractKinds.ScenePresentationParameterCurveClip;
        public override string CurveChannelPrefix => "scene-presentation";
        public override IReadOnlyList<TimelineCurveChannelDescriptor> CurveChannels => new[]
        {
            new TimelineCurveChannelDescriptor(new TimelineCurveChannelId("scene-presentation.value"),
                typeof(ScenePresentationParameterCurveClip), "Value", new Color32(132, 224, 184, 255),
                TimelineCurveValueDomain.Unbounded(0f, string.Empty),
                () => AnimationCurve.Linear(0f, 0f, 1f, 0f),
                owner => ((ScenePresentationParameterCurveClip)owner).ValueCurve,
                (owner, curve) => ((ScenePresentationParameterCurveClip)owner).SetValueCurve(curve))
        };
        public override TimelineContentParameters CaptureContentParameters() => new TimelineContentSceneParameter(
            TargetBindingId, ParameterBindingId, ParameterValueKind);
        public string TargetBindingId => m_TargetBindingId ?? string.Empty;
        public string ParameterBindingId => m_ParameterBindingId ?? string.Empty;
        public TimelineBindingValueKind ParameterValueKind
        {
            get
            {
                return Track?.Timeline != null &&
                       Track.Timeline.TryGetExternalBinding(ParameterBindingId, out TimelineExternalBindingDeclaration binding)
                    ? binding.ValueKind
                    : TimelineBindingValueKind.Scalar;
            }
        }
        public AnimationCurve ValueCurve => m_ValueCurve;
        public IReadOnlyList<TimelineExternalBindingUse> ExternalBindingUses => new[]
        {
            new TimelineExternalBindingUse(TargetBindingId, TimelineBindingValueKind.Target, TimelineBindingAccess.Input, TimelineBindingLifetime.Call),
            new TimelineExternalBindingUse(ParameterBindingId, ParameterValueKind, TimelineBindingAccess.Write, TimelineBindingLifetime.Tick, TargetBindingId)
        };

        public void ConfigureBindings(string targetBindingId, string parameterBindingId, AnimationCurve valueCurve)
        {
            m_TargetBindingId = targetBindingId?.Trim() ?? string.Empty;
            m_ParameterBindingId = parameterBindingId?.Trim() ?? string.Empty;
            m_ValueCurve = valueCurve ?? throw new ArgumentNullException(nameof(valueCurve));
#if UNITY_EDITOR
            OnNameChanged?.Invoke();
#endif
        }

        public void SetValueCurve(AnimationCurve valueCurve)
        {
            m_ValueCurve = valueCurve ?? throw new ArgumentNullException(nameof(valueCurve));
#if UNITY_EDITOR
            OnNameChanged?.Invoke();
#endif
        }

#if UNITY_EDITOR
        public override ClipCapabilities Capabilities => ClipCapabilities.Resizable | ClipCapabilities.TickQuantized;
        public override TimelineAuthoringClipConfiguration CaptureAuthoringDefaults()
        {
            TimelineAuthoringClipConfiguration defaults = base.CaptureAuthoringDefaults();
            defaults.Set("valueCurve", AnimationCurve.Linear(0f, 0f, 1f, 0f));
            return defaults;
        }

        public ScenePresentationParameterCurveClip(Track track, OrchestrationTime time) : base(track, time)
        {
        }
#endif
    }

    public static class ScenePresentationTimelineContracts
    {
        public static readonly ITimelineContractProvider Provider = new TimelineContractProvider(
            new[]
            {
                new TimelineTrackContract(
                    TimelineContractKinds.ScenePresentationParameterTrack,
                    TimelineTrackOverlapPolicy.Parallel,
                    TimelineContentKind.Sampled,
                    TimelineExecutionDomain.Presentation,
                    TimelineOutputKind.PresentationEvent,
                    null,
                    TimelineContractKinds.ScenePresentationParameterCurveClip)
            },
            new[]
            {
                new TimelineClipContract(
                    TimelineContractKinds.ScenePresentationParameterCurveClip,
                    TimelineContractKinds.ScenePresentationParameterTrack,
                    TimelineClipExecutionPhase.Commit,
                    TimelineContentKind.Sampled,
                    true,
                    true,
                    TimelineExecutionDomain.Presentation,
                    TimelineOutputKind.PresentationEvent,
                    ValidateClip)
            },
            ValidateTimeline);

        static void ValidateClip(Clip clip, List<string> errors)
        {
            ScenePresentationParameterCurveClip parameterClip = clip as ScenePresentationParameterCurveClip;
            if (parameterClip == null || parameterClip.ValueCurve == null || parameterClip.ValueCurve.length == 0)
                errors?.Add($"Timeline scene presentation clip '{clip?.AuthoringId}' requires a value curve.");
            if (parameterClip?.Track?.Timeline != null)
            {
                if (!parameterClip.Track.Timeline.TryGetExternalBinding(parameterClip.TargetBindingId, out TimelineExternalBindingDeclaration target) ||
                    target.ValueKind != TimelineBindingValueKind.Target ||
                    target.Access != TimelineBindingAccess.Input ||
                    target.Lifetime != TimelineBindingLifetime.Call)
                    errors?.Add($"Timeline scene presentation clip '{parameterClip.AuthoringId}' requires a Call Target binding.");
                if (!parameterClip.Track.Timeline.TryGetExternalBinding(parameterClip.ParameterBindingId, out TimelineExternalBindingDeclaration parameter) ||
                    (parameter.ValueKind != TimelineBindingValueKind.Scalar && parameter.ValueKind != TimelineBindingValueKind.Boolean) ||
                    parameter.Access != TimelineBindingAccess.Write ||
                    parameter.Lifetime != TimelineBindingLifetime.Tick ||
                    string.IsNullOrEmpty(parameter.ParameterId))
                    errors?.Add($"Timeline scene presentation clip '{parameterClip.AuthoringId}' requires a Tick Scalar or Boolean write binding.");
                else if (!string.Equals(parameterClip.TargetBindingId, FindTargetBinding(parameterClip.Track.Timeline, parameterClip.ParameterBindingId), StringComparison.Ordinal))
                    errors?.Add($"Timeline scene presentation clip '{parameterClip.AuthoringId}' parameter binding '{parameterClip.ParameterBindingId}' does not target '{parameterClip.TargetBindingId}'.");
            }
        }

        static string FindTargetBinding(TimelineData timeline, string parameterBindingId)
        {
            for (int trackIndex = 0; trackIndex < timeline.Tracks.Count; trackIndex++)
            {
                if (timeline.Tracks[trackIndex] == null)
                    continue;
                for (int clipIndex = 0; clipIndex < timeline.Tracks[trackIndex].Clips.Count; clipIndex++)
                {
                    if (timeline.Tracks[trackIndex].Clips[clipIndex] is not ITimelineExternalBindingUseSource source)
                        continue;
                    IReadOnlyList<TimelineExternalBindingUse> uses = source.ExternalBindingUses;
                    for (int useIndex = 0; useIndex < uses.Count; useIndex++)
                        if (string.Equals(uses[useIndex].BindingId, parameterBindingId, StringComparison.Ordinal))
                            return uses[useIndex].TargetBindingId;
                }
            }
            return string.Empty;
        }

        static void ValidateTimeline(TimelineData timeline, List<string> errors)
        {
            if (timeline?.Tracks == null)
                return;
            var clips = new List<ScenePresentationParameterCurveClip>();
            for (int trackIndex = 0; trackIndex < timeline.Tracks.Count; trackIndex++)
            {
                if (timeline.Tracks[trackIndex] is not ScenePresentationParameterTrack track || track.Clips == null)
                    continue;
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                    if (track.Clips[clipIndex] is ScenePresentationParameterCurveClip clip)
                        clips.Add(clip);
            }
            for (int leftIndex = 0; leftIndex < clips.Count; leftIndex++)
            {
                ScenePresentationParameterCurveClip left = clips[leftIndex];
                for (int rightIndex = leftIndex + 1; rightIndex < clips.Count; rightIndex++)
                {
                    ScenePresentationParameterCurveClip right = clips[rightIndex];
                    if (left.EndTime <= right.StartTime || right.EndTime <= left.StartTime ||
                        !string.Equals(left.TargetBindingId, right.TargetBindingId, StringComparison.Ordinal))
                        continue;
                    if (string.Equals(left.ParameterBindingId, right.ParameterBindingId, StringComparison.Ordinal))
                    {
                        errors?.Add($"Timeline scene presentation content has overlapping writes for target '{left.TargetBindingId}' and parameter binding '{left.ParameterBindingId}'.");
                        continue;
                    }
                    if (!timeline.TryGetExternalBinding(left.ParameterBindingId, out TimelineExternalBindingDeclaration leftBinding) ||
                        !timeline.TryGetExternalBinding(right.ParameterBindingId, out TimelineExternalBindingDeclaration rightBinding))
                        continue;
                    if (string.Equals(leftBinding.Domain, rightBinding.Domain, StringComparison.Ordinal) &&
                        string.Equals(leftBinding.ParameterId, rightBinding.ParameterId, StringComparison.Ordinal) &&
                        leftBinding.ValueKind == rightBinding.ValueKind)
                        errors?.Add($"Timeline scene presentation content has overlapping writes for target '{left.TargetBindingId}' and parameter '{leftBinding.ParameterId}'.");
                }
            }
        }
    }
}

