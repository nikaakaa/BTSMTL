using System;
using ThirdPersonSimulation.Fixed;
using System.Collections.Generic;
using BTSMTL.Diagnostics;
using ThirdPersonCamera;
using UnityEngine;

namespace BTSMTL.Timeline
{
    [TrackGroup("Camera"), ScriptGuid("8b4e2c6a91d3f75e0a62c48b19d7e35f"), Ordered(7), Color(236, 190, 120)]
    public sealed class CameraEffectTrack : Track
    {
        public override string ContractKind => TimelineContractKinds.CameraEffectTrack;

#if UNITY_EDITOR
        public override Type ClipType => typeof(CameraEffectClip);

        public override Clip AddClip(UnityEngine.Object referenceObject, FixedScalar time)
        {
            if (referenceObject is not CameraEffectAsset effect)
                throw new ArgumentException("Camera effect clip requires a CameraEffectAsset.", nameof(referenceObject));
            var clip = new CameraEffectClip(this, time)
            {
                Effect = effect
            };
            clip.RegenerateAuthoringIdentity();
            m_Clips.Add(clip);
            return clip;
        }
#endif
    }

    [ScriptGuid("8b4e2c6a91d3f75e0a62c48b19d7e35f"), Color(236, 190, 120)]
    public sealed class CameraEffectClip : Clip, ITimelineContentClosureSource
    {
        public override string ContractKind => TimelineContractKinds.CameraEffectClip;
        public override TimelineContentParameters CaptureContentParameters() => new TimelineContentCameraEffect(
            Effect switch
            {
                CameraOverrideTrackAsset => TimelineCameraResourceKind.Override,
                CameraZoomAsset => TimelineCameraResourceKind.Zoom,
                CameraStretchAsset => TimelineCameraResourceKind.Stretch,
                CameraShotAsset => TimelineCameraResourceKind.Shot,
                _ => throw new InvalidOperationException($"Camera effect clip '{AuthoringId}' has no supported resource.")
            }, Effect.EffectId, Effect.EffectPriority);

        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public CameraEffectAsset Effect;

        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve WeightCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseInCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public AnimationCurve EaseOutCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        public void CollectContentClosure(TimelineContentClosureBuilder builder)
        {
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));
            string resourceId = Effect ? Effect.EffectId : string.Empty;
            if (string.IsNullOrWhiteSpace(resourceId))
            {
                builder.AddError("timeline_camera_resource_missing", AuthoringId, "Camera effect clip has no formal resource identity.");
                return;
            }
            UnityEngine.Object resource = Effect;
            builder.AddDependency(
                $"camera:{ContractKind}:{resourceId}",
                ContractKind,
                $"clip:{AuthoringId}",
                SourceContentHasher.Hash(JsonUtility.ToJson(resource)));
        }

#if UNITY_EDITOR
        public override ClipCapabilities Capabilities => ClipCapabilities.Resizable | ClipCapabilities.Mixable;
        public CameraEffectClip(Track track, FixedScalar time) : base(track, time) { }
#endif
    }
}
