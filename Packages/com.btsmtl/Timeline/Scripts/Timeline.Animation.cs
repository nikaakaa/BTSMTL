using System;
using ThirdPersonSimulation.Fixed;
using System.Collections.Generic;
using ThirdPersonSimulation;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [TrackGroup("Base"), ScriptGuid("3f0d14cafa6f2c84389c42789ec00083"), IconGuid("e6435fa591ae4414eb0f26dc6410086e"), Ordered(0), Color(127, 253, 228)]
#if UNITY_EDITOR
    [TimelineAuthoringTrackField("animationChannelId", "skill_timeline_animation_channel_invalid", "AnimationTrack必须声明稳定AnimationChannel identity。")]
    [TimelineAuthoringTrackField("animationSlotId", "skill_timeline_animation_slot_invalid", "AnimationTrack必须声明稳定AnimationSlot identity。")]
#endif
    public partial class AnimationTrack : Track
#if UNITY_EDITOR
        , ITimelineAuthoringTrackFieldSink
#endif
    {
        public override string ContractKind => TimelineContractKinds.AnimationTrack;

        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
        string m_AnimationChannelId = string.Empty;
        [SerializeField, ShowInInspector, OnValueChanged("RebindTimeline")]
        string m_AnimationSlotId = string.Empty;

        public AnimationChannelId AnimationChannelId => string.IsNullOrWhiteSpace(m_AnimationChannelId)
            ? default
            : new AnimationChannelId(m_AnimationChannelId);

        public string AnimationSlotId => m_AnimationSlotId ?? string.Empty;

#if UNITY_EDITOR
        public void SetAnimationChannelId(AnimationChannelId animationChannelId)
        {
            m_AnimationChannelId = animationChannelId.IsValid
                ? animationChannelId.Value.ToString()
                : throw new ArgumentException("Animation Channel identity is invalid.", nameof(animationChannelId));
            RebindTimeline();
        }

        public void SetAnimationSlotId(string animationSlotId)
        {
            m_AnimationSlotId = animationSlotId?.Trim() ?? string.Empty;
            RebindTimeline();
        }

        public void ApplyAuthoringField(string fieldId, string value)
        {
            switch (fieldId)
            {
                case "animationChannelId":
                    SetAnimationChannelId(new AnimationChannelId(value));
                    return;
                case "animationSlotId":
                    SetAnimationSlotId(value);
                    return;
                default:
                    throw new ArgumentException($"Unknown AnimationTrack authoring field '{fieldId}'.", nameof(fieldId));
            }
        }

#endif

#if UNITY_EDITOR

        public override Type ClipType => typeof(AnimationClip);
        public override Clip AddClip(UnityEngine.Object referenceObject, FixedScalar time)
        {
            AnimationClip clip = new AnimationClip(referenceObject as UnityEngine.AnimationClip, this, time);
            clip.RegenerateAuthoringIdentity();
            m_Clips.Add(clip);
            return clip;
        }
        public override bool DragValid()
        {
            return UnityEditor.DragAndDrop.objectReferences.Length == 1 &&
                   UnityEditor.DragAndDrop.objectReferences[0] as UnityEngine.AnimationClip;
        }
#endif
    }

    [ScriptGuid("3f0d14cafa6f2c84389c42789ec00083"), Color(127, 253, 228)]
#if UNITY_EDITOR
    [TimelineAuthoringProperty("extraPolationMode", typeof(ExtraPolationMode))]
    [TimelineAuthoringProperty(
        "blendProfileId",
        TimelineAuthoringPropertyKind.Text,
        Trimmed = true)]
#endif
    public partial class AnimationClip : Clip
    {
        public override string ContractKind => TimelineContractKinds.AnimationClip;
        public override TimelineContentParameters CaptureContentParameters() => new TimelineContentAnimation(
            $"timeline:{Track.Timeline.AuthoringId}/track:{Track.AuthoringId}/clip:{AuthoringId}",
            ((AnimationTrack)Track).AnimationChannelId, ((AnimationTrack)Track).AnimationSlotId,
            BlendProfileId, ExtraPolationMode);

        [ShowInInspector, OnValueChanged("OnClipChanged", "RebindTimeline")]
        public UnityEngine.AnimationClip Clip;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public ExtraPolationMode ExtraPolationMode;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve WeightCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public string BlendProfileId = string.Empty;

        public override string Name => Clip ? Clip.name : base.Name;
#if UNITY_EDITOR
        public override FixedScalar Length => Clip
            ? FixedScalar.FromDouble(Clip.length)
            : base.Length;
        public override ClipCapabilities Capabilities => ClipCapabilities.Resizable | ClipCapabilities.Mixable | ClipCapabilities.ClipInable;
        public AnimationClip(Track track, FixedScalar time) : base(track, time) { }
        public AnimationClip(UnityEngine.AnimationClip clip, Track track, FixedScalar time) : base(track, time)
        {
            Clip = clip;
            ConfigureTimeRange(time, time + Length);
        }
        void OnClipChanged()
        {
            OnNameChanged?.Invoke();
        }
#endif
    }

    public static class AnimationTimelineContracts
    {
        public static readonly ITimelineContractProvider Provider = new TimelineContractProvider(
            new[]
            {
                new TimelineTrackContract(
                    TimelineContractKinds.AnimationTrack,
                    TimelineTrackOverlapPolicy.Blend,
                    TimelineCapability.Animation,
                    TimelineExecutionDomain.Presentation,
                    TimelineOutputKind.PresentationEvent,
                    TimelineContractKinds.AnimationClip)
            },
            new[]
            {
                new TimelineClipContract(
                    TimelineContractKinds.AnimationClip,
                    TimelineContractKinds.AnimationTrack,
                    TimelineClipExecutionPhase.Commit,
                    TimelineCapability.Animation,
                    true,
                    true,
                    TimelineExecutionDomain.Presentation,
                    TimelineOutputKind.PresentationEvent)
            },
            ValidateAnimationContent);

        static void ValidateAnimationContent(TimelineData timeline, List<string> errors)
        {
            for (int trackIndex = 0; trackIndex < timeline.Tracks.Count; trackIndex++)
            {
                if (timeline.Tracks[trackIndex] is not AnimationTrack track)
                    continue;
                if (!track.AnimationChannelId.IsValid)
                    errors?.Add($"Timeline '{timeline.Name}' AnimationTrack '{track.AuthoringId}' has no Animation Channel.");
                if (string.IsNullOrWhiteSpace(track.AnimationSlotId) || track.AnimationSlotId != track.AnimationSlotId.Trim())
                    errors?.Add($"Timeline '{timeline.Name}' AnimationTrack '{track.AuthoringId}' has no Animation Slot.");
                for (int clipIndex = 0; clipIndex < track.Clips.Count; clipIndex++)
                {
                    if (track.Clips[clipIndex] is not AnimationClip clip)
                        continue;
                    if (string.IsNullOrWhiteSpace(clip.BlendProfileId) || clip.BlendProfileId != clip.BlendProfileId.Trim())
                        errors?.Add($"Timeline '{timeline.Name}' AnimationClip '{clip.AuthoringId}' has no Blend Profile.");
                }
            }
        }
    }
}
