using System;
using Photon.Deterministic;
using System.Collections.Generic;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [TrackGroup("Base"), ScriptGuid("54a348faecf94a2ea8ec2b06146e74c2"), Ordered(4), Color(180, 160, 255)]
    public sealed class CameraStateTrack : Track
    {
        public override string ContractKind => TimelineContractKinds.CameraStateTrack;

#if UNITY_EDITOR
        public override Type ClipType => typeof(CameraStateClip);
#endif
    }

    [ScriptGuid("54a348faecf94a2ea8ec2b06146e74c2"), Color(180, 160, 255)]
#if UNITY_EDITOR
    [TimelineAuthoringProperty("mode", typeof(TimelineCameraMode))]
    [TimelineAuthoringProperty("sequenceId", TimelineAuthoringPropertyKind.Text, Trimmed = true)]
    [TimelineAuthoringProperty("priority", TimelineAuthoringPropertyKind.Integer)]
    [TimelineAuthoringProperty("blendInSeconds", TimelineAuthoringPropertyKind.Float, HasMinimum = true, Minimum = 0d, Finite = true)]
    [TimelineAuthoringProperty("blendOutSeconds", TimelineAuthoringPropertyKind.Float, HasMinimum = true, Minimum = 0d, Finite = true)]
    [TimelineAuthoringProperty("targetKey", TimelineAuthoringPropertyKind.Text, Optional = true, Trimmed = true)]
    [TimelineAuthoringProperty("interruptPolicy", typeof(TimelineCameraInterruptPolicy))]
#endif
    public sealed class CameraStateClip : Clip
    {
        public override string ContractKind => TimelineContractKinds.CameraStateClip;
        public override TimelineContentParameters CaptureContentParameters() => new TimelineContentCameraState(
            Mode, SequenceId, Priority, BlendInSeconds, BlendOutSeconds, TargetKey, InterruptPolicy);

        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public TimelineCameraMode Mode = TimelineCameraMode.SkillCloseup;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public string SequenceId;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public int Priority = 100;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public float BlendInSeconds = 0.15f;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public float BlendOutSeconds = 0.2f;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public string TargetKey;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public TimelineCameraInterruptPolicy InterruptPolicy = TimelineCameraInterruptPolicy.BlendOut;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve WeightCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

#if UNITY_EDITOR
        public override ClipCapabilities Capabilities => ClipCapabilities.Resizable | ClipCapabilities.Mixable;

        public CameraStateClip(Track track, FP time) : base(track, time)
        {
        }
#endif
    }

    [TrackGroup("Base"), ScriptGuid("54a348faecf94a2ea8ec2b06146e74c2"), Ordered(6), Color(170, 225, 255)]
    public sealed class CameraResponseTrack : Track
    {
        public override string ContractKind => TimelineContractKinds.CameraResponseTrack;

#if UNITY_EDITOR
        public override Type ClipType => typeof(CameraResponseClip);
#endif
    }

    [ScriptGuid("54a348faecf94a2ea8ec2b06146e74c2"), Color(170, 225, 255)]
#if UNITY_EDITOR
    [TimelineAuthoringProperty("lookResponse", typeof(TimelineCameraLookResponseMode))]
    [TimelineAuthoringProperty("manualOrbitWeight", TimelineAuthoringPropertyKind.Float, HasMinimum = true, Minimum = 0d, HasMaximum = true, Maximum = 1d, Finite = true)]
    [TimelineAuthoringProperty("pitchResponseWeight", TimelineAuthoringPropertyKind.Float, HasMinimum = true, Minimum = 0d, HasMaximum = true, Maximum = 1d, Finite = true)]
    [TimelineAuthoringProperty("yawResponseWeight", TimelineAuthoringPropertyKind.Float, HasMinimum = true, Minimum = 0d, HasMaximum = true, Maximum = 1d, Finite = true)]
    [TimelineAuthoringProperty("priority", TimelineAuthoringPropertyKind.Integer)]
#endif
    public sealed class CameraResponseClip : Clip
    {
        public override string ContractKind => TimelineContractKinds.CameraResponseClip;
        public override TimelineContentParameters CaptureContentParameters() => new TimelineContentCameraResponse(
            LookResponse, ManualOrbitWeight, PitchResponseWeight, YawResponseWeight, Priority);

        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public TimelineCameraLookResponseMode LookResponse = TimelineCameraLookResponseMode.Suppressed;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public float ManualOrbitWeight;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public float PitchResponseWeight = 1f;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public float YawResponseWeight = 1f;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public int Priority = 100;
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve WeightCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

#if UNITY_EDITOR
        public override ClipCapabilities Capabilities => ClipCapabilities.Resizable | ClipCapabilities.Mixable;

        public CameraResponseClip(Track track, FP time) : base(track, time)
        {
        }
#endif
    }

    public static class CameraTimelineContracts
    {
        public static readonly ITimelineContractProvider Provider = new TimelineContractProvider(
            new[]
            {
                new TimelineTrackContract(
                    TimelineContractKinds.CameraStateTrack,
                    TimelineTrackOverlapPolicy.Blend,
                    TimelineCapability.Camera,
                    TimelineExecutionDomain.Presentation,
                    TimelineOutputKind.PresentationEvent,
                    TimelineContractKinds.CameraStateClip),
                new TimelineTrackContract(
                    TimelineContractKinds.CameraResponseTrack,
                    TimelineTrackOverlapPolicy.Blend,
                    TimelineCapability.Camera,
                    TimelineExecutionDomain.Presentation,
                    TimelineOutputKind.PresentationEvent,
                    TimelineContractKinds.CameraResponseClip),
                new TimelineTrackContract(
                    TimelineContractKinds.CameraEffectTrack,
                    TimelineTrackOverlapPolicy.Parallel,
                    TimelineCapability.Camera,
                    TimelineExecutionDomain.Presentation,
                    TimelineOutputKind.PresentationEvent,
                    TimelineContractKinds.CameraEffectClip)
            },
            new[]
            {
                new TimelineClipContract(
                    TimelineContractKinds.CameraStateClip,
                    TimelineContractKinds.CameraStateTrack,
                    TimelineClipExecutionPhase.Commit,
                    TimelineCapability.Camera,
                    true,
                    true,
                    TimelineExecutionDomain.Presentation,
                    TimelineOutputKind.PresentationEvent),
                new TimelineClipContract(
                    TimelineContractKinds.CameraResponseClip,
                    TimelineContractKinds.CameraResponseTrack,
                    TimelineClipExecutionPhase.Commit,
                    TimelineCapability.Camera,
                    true,
                    true,
                    TimelineExecutionDomain.Presentation,
                    TimelineOutputKind.PresentationEvent),
                new TimelineClipContract(
                    TimelineContractKinds.CameraEffectClip,
                    TimelineContractKinds.CameraEffectTrack,
                    TimelineClipExecutionPhase.Commit,
                    TimelineCapability.Camera,
                    true,
                    true,
                    TimelineExecutionDomain.Presentation,
                    TimelineOutputKind.PresentationEvent)
            });
    }

}
