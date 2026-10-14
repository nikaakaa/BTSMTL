using ThirdPersonSimulation.QuantumNumerics;
using System;
using Photon.Deterministic;
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

        public override Clip AddClip(UnityEngine.Object referenceObject, FP time)
        {
            if (referenceObject is not CameraEffectAsset effect)
                throw new ArgumentException("Camera effect clip requires a CameraEffectAsset.", nameof(referenceObject));
            var clip = new CameraEffectClip(this, time)
            {
                Effect = effect
            };
            if (effect is ITimelineCameraSourceAsset source)
            {
                clip.ConfigureSourceRange(0f, source.SourceDuration);
                clip.ConfigureTimeRange(time, time + QuantumScalarBoundary.ConvertExternal(source.SourceDuration, nameof(CameraEffectTrack)));
            }
            clip.RegenerateAuthoringIdentity();
            m_Clips.Add(clip);
            return clip;
        }
#endif
    }

    [ScriptGuid("8b4e2c6a91d3f75e0a62c48b19d7e35f"), Color(236, 190, 120)]
#if UNITY_EDITOR
    [TimelineAuthoringProperty("effect", TimelineAuthoringPropertyKind.Object)]
    [TimelineAuthoringProperty("sourceStartTime", TimelineAuthoringPropertyKind.Float, Finite = true, SourceMappingOnly = true)]
    [TimelineAuthoringProperty("sourceEndTime", TimelineAuthoringPropertyKind.Float, Finite = true, SourceMappingOnly = true)]
#endif
    public sealed class CameraEffectClip : Clip, ITimelineContentClosureSource
    {
        public override string ContractKind => TimelineContractKinds.CameraEffectClip;
        public override TimelineContentParameters CaptureContentParameters() => Effect is ITimelineCameraEffectAsset resource
            ? new TimelineContentCameraEffect(resource.ResourceKind, Effect.EffectId, Effect.EffectPriority,
                m_SourceStartTime, m_SourceEndTime, Effect is ITimelineCameraSourceAsset source ? source.ContentRevision : string.Empty)
            : throw new InvalidOperationException($"Camera effect clip '{AuthoringId}' has no supported resource.");

        [ShowInInspector, OnValueChanged("RebindTimeline")]
        public CameraEffectAsset Effect;

        [SerializeField] float m_SourceStartTime;
        [SerializeField] float m_SourceEndTime;
        public float SourceStartTime => m_SourceStartTime;
        public float SourceEndTime => m_SourceEndTime;

        public override void Init(Track track)
        {
            base.Init(track);
            if (Effect is ITimelineCameraSourceAsset source &&
                (DurationTime <= FP._0 || !float.IsFinite(m_SourceStartTime) || !float.IsFinite(m_SourceEndTime) ||
                 m_SourceStartTime < 0f || m_SourceEndTime <= m_SourceStartTime || m_SourceEndTime > source.SourceDuration))
                throw new InvalidOperationException($"镜头片段 '{AuthoringId}' 的源使用范围无效。");
        }

#if UNITY_EDITOR
        public void ConfigureSourceRange(float sourceStartTime, float sourceEndTime)
        {
            m_SourceStartTime = sourceStartTime;
            m_SourceEndTime = sourceEndTime;
        }
#endif

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
        public CameraEffectClip(Track track, FP time) : base(track, time) { }
#endif
    }
}
