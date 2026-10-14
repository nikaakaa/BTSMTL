using ThirdPersonSimulation.QuantumNumerics;
#if UNITY_EDITOR
using System;
using Photon.Deterministic;
using System.Collections.Generic;
using System.Linq;
using ThirdPersonCamera;
using ThirdPersonCharacter.Pipeline.Motion.RootMotion;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineAuthoringReferenceValue
    {
        public TimelineAuthoringReferenceValue(
            UnityEngine.Object asset,
            string errorCode = "",
            string errorMessage = "")
        {
            Asset = asset;
            ErrorCode = errorCode ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public UnityEngine.Object Asset { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public bool IsMissing => !Asset && !string.IsNullOrEmpty(ErrorCode);
    }

    public readonly struct TimelineAuthoringPropertyValue
    {
        public TimelineAuthoringPropertyValue(
            string propertyId,
            TimelineAuthoringPropertyKind kind,
            object value)
        {
            PropertyId = string.IsNullOrWhiteSpace(propertyId)
                ? throw new ArgumentException("Timeline authoring property identity is missing.", nameof(propertyId))
                : propertyId;
            Kind = kind;
            Value = value;
        }

        public string PropertyId { get; }
        public TimelineAuthoringPropertyKind Kind { get; }
        public object Value { get; }
    }

    public static class TimelineAuthoringPropertyContract
    {
        public static IReadOnlyList<TimelineAuthoringPropertyValue> Read(Clip clip)
        {
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            TimelineAuthoringClipConfiguration configuration = TimelineAuthoringClipBinding.Read(clip);
            var result = new List<TimelineAuthoringPropertyValue>();
            TimelineContractCatalog catalog = TimelineTreeContractComposition.Create();
            if (clip.Track == null ||
                !catalog.RequireTrack(clip.Track.ContractKind).SupportsExecutionDomain(clip.ExecutionDomain) ||
                !catalog.RequireClip(clip.ContractKind).SupportsExecutionDomain(clip.ExecutionDomain))
                throw new InvalidOperationException($"Timeline clip '{clip.AuthoringId}' has an unsupported execution domain '{clip.ExecutionDomain}'.");
            foreach (TimelineAuthoringPropertyAttribute property in clip.GetType()
                         .GetCustomAttributes(typeof(TimelineAuthoringPropertyAttribute), true)
                         .OfType<TimelineAuthoringPropertyAttribute>()
                         .OrderBy(value => value.PropertyId, StringComparer.Ordinal))
            {
                if (!property.Supports(clip))
                    continue;
                object value = ReadValue(configuration, property.PropertyId);
                if (value is AnimationCurve)
                    continue;
                Add(result, clip, property.PropertyId, property.Kind, value);
            }
            if (clip is TreeClip tree)
            {
                Add(result, clip, "executionPhase", TimelineAuthoringPropertyKind.Enum, tree.ExecutionPhase);
                Add(result, clip, "exitSource", TimelineAuthoringPropertyKind.Enum, tree.ClipExitSource);
                if (tree.AssetTree)
                    Add(result, clip, "assetTree", TimelineAuthoringPropertyKind.Object, tree.AssetTree);
            }
            return result;
        }

        public static UnityEngine.Object ReferenceAsset(Clip clip)
        {
            if (clip is TreeClip tree && tree.AssetTree)
                return tree.AssetTree;
            return TimelineAuthoringClipBinding.SourceAsset(clip);
        }

        public static TimelineAuthoringReferenceValue Reference(Clip clip)
        {
            UnityEngine.Object asset = ReferenceAsset(clip);
            if (asset)
                return new TimelineAuthoringReferenceValue(asset);
            if (clip is MotionCurveClip)
                return new TimelineAuthoringReferenceValue(
                    null,
                    "motion_curve_source_missing",
                    "MotionCurveClip缺少RootMotionCurveAsset正式源引用。");
            if (clip is TreeClip)
                return new TimelineAuthoringReferenceValue(
                    null,
                    "timeline_tree_source_unsupported",
                    "TreeClip不是正式资产节点图引用。");
            return new TimelineAuthoringReferenceValue(null);
        }

        public static FP DefaultEndTime(
            Clip clip,
            FP startTime,
            UnityEngine.Object referenceObject)
        {
            if (referenceObject is UnityEngine.AnimationClip animation)
                return startTime + QuantumScalarBoundary.ConvertExternal(animation.length, nameof(TimelineAuthoringReferenceValue));
            if (referenceObject is ITimelineCameraSourceAsset camera)
                return startTime + QuantumScalarBoundary.ConvertExternal(camera.SourceDuration, nameof(TimelineAuthoringReferenceValue));
            return startTime + (clip is SignalClip ? TimelineTimeGrid.Position(1, TimelineUtility.FrameRate) : FP._0_05);
        }

        public static bool NeedsSegmentOverride(
            Clip clip,
            UnityEngine.Object referenceObject)
        {
            if (clip is TreeClip tree &&
                tree.ClipExitSource == TimelineClipExitSource.TreeDecision)
            {
                return false;
            }
            return clip.EndTime != DefaultEndTime(clip, clip.StartTime, referenceObject) ||
                   clip.SelfEaseInTime.RawValue != 0 ||
                   clip.SelfEaseOutTime.RawValue != 0 ||
                   clip.ClipInTime.RawValue != 0;
        }

        public static void Apply(
            TimelineData timeline,
            Clip clip,
            IReadOnlyList<TimelineAuthoringPropertyValue> values)
        {
            if (timeline == null)
                throw new ArgumentNullException(nameof(timeline));
            if (clip == null)
                throw new ArgumentNullException(nameof(clip));
            TimelineAuthoringClipConfiguration configuration = DefaultConfiguration();
            TimelineTreeExecutionPhase executionPhase = TimelineTreeExecutionPhase.Commit;
            TimelineClipExitSource exitSource = TimelineClipExitSource.TreeDecision;
            UnityEngine.Object treeAsset = null;
            foreach (TimelineAuthoringPropertyValue value in values ?? Array.Empty<TimelineAuthoringPropertyValue>())
            {
                if (value.PropertyId == "executionPhase")
                    executionPhase = (TimelineTreeExecutionPhase)value.Value;
                else if (value.PropertyId == "exitSource")
                    exitSource = (TimelineClipExitSource)value.Value;
                else if (value.PropertyId == "assetTree")
                    treeAsset = (UnityEngine.Object)value.Value;
                else
                    SetValue(configuration, value.PropertyId, value.Value);
            }
            TimelineContractCatalog catalog = TimelineTreeContractComposition.Create();
            TimelineExecutionDomain executionDomain = clip.Track.ExecutionDomain;
            if (!catalog.RequireTrack(clip.Track.ContractKind).SupportsExecutionDomain(executionDomain) ||
                !catalog.RequireClip(clip.ContractKind).SupportsExecutionDomain(executionDomain))
                throw new InvalidOperationException($"Timeline clip '{clip.AuthoringId}' does not support execution domain '{executionDomain}'.");
            TimelineAuthoringClipBinding.Configure(timeline, clip, configuration);
            if (clip is TreeClip tree)
            {
                if (treeAsset)
                    tree.SetAssetTree(treeAsset as ScriptableObject);
                tree.SetExecutionPhase(executionPhase);
                tree.SetExitSource(tree.ExecutionDomain == TimelineExecutionDomain.Logic
                    ? TimelineClipExitSource.TreeDecision
                    : exitSource);
            }
        }

        static void Add(
            ICollection<TimelineAuthoringPropertyValue> result,
            Clip clip,
            string propertyId,
            TimelineAuthoringPropertyKind kind,
            object value)
        {
            if (value is UnityEngine.Object asset && !asset ||
                value == null ||
                IsDefault(clip, propertyId, value))
                return;
            result.Add(new TimelineAuthoringPropertyValue(propertyId, kind, value));
        }

        static object ReadValue(
            TimelineAuthoringClipConfiguration configuration,
            string propertyId) =>
            propertyId switch
            {
                "extraPolationMode" => configuration.Extrapolation,
                "blendProfileId" => configuration.BlendProfileId,
                "curveId" => configuration.CurveId,
                "sourceCurve" => configuration.SourceCurve,
                "sourceStartTime" => configuration.SourceStartTime,
                "sourceEndTime" => configuration.SourceEndTime,
                "space" => configuration.Space,
                "channel" => configuration.Channel,
                "blendMode" => configuration.BlendMode,
                "priority" => configuration.Priority,
                "consumeLowerChannels" => configuration.ConsumeLowerChannels,
                "mode" => configuration.CameraMode,
                "sequenceId" => configuration.CameraSequenceId,
                "blendInSeconds" => configuration.CameraBlendInSeconds,
                "blendOutSeconds" => configuration.CameraBlendOutSeconds,
                "targetKey" => configuration.CameraTargetKey,
                "interruptPolicy" => configuration.CameraInterruptPolicy,
                "resourceId" => configuration.CameraResourceId,
                "effect" => configuration.Effect,
                "lookResponse" => configuration.CameraLookResponse,
                "manualOrbitWeight" => configuration.ManualOrbitWeight,
                "pitchResponseWeight" => configuration.PitchResponseWeight,
                "yawResponseWeight" => configuration.YawResponseWeight,
                "targetBindingId" => configuration.TargetBindingId,
                "parameterBindingId" => configuration.ParameterBindingId,
                _ => throw new InvalidOperationException($"未知Timeline作者属性：{propertyId}")
            };

        static void SetValue(
            TimelineAuthoringClipConfiguration configuration,
            string propertyId,
            object value)
        {
            switch (propertyId)
            {
                case "extraPolationMode": configuration.Extrapolation = (ExtraPolationMode)value; break;
                case "blendProfileId": configuration.BlendProfileId = (string)value; break;
                case "curveId": configuration.CurveId = (string)value; break;
                case "sourceCurve": configuration.SourceCurve = (RootMotionCurveAsset)value; break;
                case "sourceStartTime": configuration.SourceStartTime = (float)value; break;
                case "sourceEndTime": configuration.SourceEndTime = (float)value; break;
                case "space": configuration.Space = (TimelineMotionContributionSpace)value; break;
                case "channel": configuration.Channel = (TimelineMotionChannel)value; break;
                case "blendMode": configuration.BlendMode = (TimelineMotionBlendMode)value; break;
                case "priority": configuration.Priority = (int)value; break;
                case "consumeLowerChannels": configuration.ConsumeLowerChannels = (bool)value; break;
                case "mode": configuration.CameraMode = (TimelineCameraMode)value; break;
                case "sequenceId": configuration.CameraSequenceId = (string)value; break;
                case "blendInSeconds": configuration.CameraBlendInSeconds = (float)value; break;
                case "blendOutSeconds": configuration.CameraBlendOutSeconds = (float)value; break;
                case "targetKey": configuration.CameraTargetKey = (string)value; break;
                case "interruptPolicy": configuration.CameraInterruptPolicy = (TimelineCameraInterruptPolicy)value; break;
                case "resourceId": configuration.CameraResourceId = (string)value; break;
                case "lookResponse": configuration.CameraLookResponse = (TimelineCameraLookResponseMode)value; break;
                case "manualOrbitWeight": configuration.ManualOrbitWeight = (float)value; break;
                case "pitchResponseWeight": configuration.PitchResponseWeight = (float)value; break;
                case "yawResponseWeight": configuration.YawResponseWeight = (float)value; break;
                case "targetBindingId": configuration.TargetBindingId = (string)value; break;
                case "parameterBindingId": configuration.ParameterBindingId = (string)value; break;
                case "effect": configuration.Effect = (CameraEffectAsset)value; break;
                default: throw new InvalidOperationException($"未知Timeline作者属性：{propertyId}");
            }
        }

        static TimelineAuthoringClipConfiguration DefaultConfiguration() =>
            new TimelineAuthoringClipConfiguration
            {
                Extrapolation = default,
                BlendProfileId = string.Empty,
                CurveId = "MotionCurve",
                Space = TimelineMotionContributionSpace.Local,
                Channel = TimelineMotionChannel.Action,
                BlendMode = TimelineMotionBlendMode.Override,
                Priority = 100,
                ConsumeLowerChannels = true,
                CameraMode = TimelineCameraMode.SkillCloseup,
                CameraSequenceId = string.Empty,
                CameraBlendInSeconds = 0.15f,
                CameraBlendOutSeconds = 0.2f,
                CameraTargetKey = string.Empty,
                CameraInterruptPolicy = TimelineCameraInterruptPolicy.BlendOut,
                CameraResourceId = string.Empty,
                CameraLookResponse = TimelineCameraLookResponseMode.Suppressed,
                ManualOrbitWeight = 0f,
                PitchResponseWeight = 1f,
                YawResponseWeight = 1f,
                TargetBindingId = "target",
                ParameterBindingId = "openAmount",
                CameraWeightCurve = DefaultCurve(TimelineCurveChannelCatalog.CameraEffectWeight),
                CameraEaseInCurve = DefaultCurve(TimelineCurveChannelCatalog.CameraEffectEaseIn),
                CameraEaseOutCurve = DefaultCurve(TimelineCurveChannelCatalog.CameraEffectEaseOut),
                ValueCurve = DefaultCurve(TimelineCurveChannelCatalog.ScenePresentationValue)
            };

        static AnimationCurve DefaultCurve(TimelineCurveChannelId id) =>
            TimelineCurveChannelCatalog.Require(id.Value).CreateDefaultCurve();

        static bool IsDefault(Clip clip, string propertyId, object value)
        {
            object expected = propertyId switch
            {
                "extraPolationMode" => default(ExtraPolationMode),
                "blendProfileId" => string.Empty,
                "curveId" => "MotionCurve",
                "sourceStartTime" => 0f,
                "sourceEndTime" => 0f,
                "space" => TimelineMotionContributionSpace.Local,
                "channel" => TimelineMotionChannel.Action,
                "blendMode" => TimelineMotionBlendMode.Override,
                "priority" => 100,
                "consumeLowerChannels" => true,
                "cueId" => "Cue",
                "cueType" => "Gameplay",
                "mode" => TimelineCameraMode.SkillCloseup,
                "sequenceId" => string.Empty,
                "blendInSeconds" => 0.15f,
                "blendOutSeconds" => 0.2f,
                "targetKey" => string.Empty,
                "interruptPolicy" => TimelineCameraInterruptPolicy.BlendOut,
                "resourceId" => string.Empty,
                "intensity" => 1f,
                "durationSeconds" => 0.2f,
                "lookResponse" => TimelineCameraLookResponseMode.Suppressed,
                "manualOrbitWeight" => 0f,
                "pitchResponseWeight" => 1f,
                "yawResponseWeight" => 1f,
                "targetBindingId" => "target",
                "parameterBindingId" => "openAmount",
                "executionPhase" => TimelineTreeExecutionPhase.Commit,
                "exitSource" => TimelineClipExitSource.TreeDecision,
                _ => null
            };
            return expected != null && ValuesEqual(value, expected);
        }

        static bool ValuesEqual(object left, object right)
        {
            if (left is Vector2 leftVector && right is Vector2 rightVector)
                return leftVector == rightVector;
            if (left is Enum leftEnum && right is Enum rightEnum)
                return leftEnum.GetType() == rightEnum.GetType() && leftEnum.Equals(rightEnum);
            return Equals(left, right);
        }
    }
}
#endif
