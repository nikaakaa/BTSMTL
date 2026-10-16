#if UNITY_EDITOR
using System;
using BTSMTL.Orchestration;
using System.Collections.Generic;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public readonly struct TimelineAuthoringReferenceValue
    {
        public TimelineAuthoringReferenceValue(UnityEngine.Object asset, string errorCode = "", string errorMessage = "")
        {
            Asset = asset;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
        }
        public UnityEngine.Object Asset { get; }
        public string ErrorCode { get; }
        public string ErrorMessage { get; }
        public bool IsMissing => !Asset && !string.IsNullOrEmpty(ErrorCode);
    }

    public readonly struct TimelineAuthoringPropertyValue
    {
        public TimelineAuthoringPropertyValue(string propertyId, TimelineAuthoringPropertyKind kind, object value)
        {
            PropertyId = propertyId;
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
            TimelineAuthoringClipConfiguration configuration = TimelineAuthoringClipBinding.Read(clip);
            TimelineAuthoringClipConfiguration defaults = TimelineAuthoringClipBinding.Defaults(clip);
            var result = new List<TimelineAuthoringPropertyValue>();
            foreach (TimelineAuthoringPropertyAttribute property in TimelineAuthoringClipBinding.Properties(clip))
            {
                if (!property.Supports(clip))
                    continue;
                object value = configuration[property.PropertyId];
                if (value is AnimationCurve || value == null ||
                    value is UnityEngine.Object asset && !asset || ValuesEqual(value, defaults[property.PropertyId]))
                    continue;
                result.Add(new TimelineAuthoringPropertyValue(property.PropertyId, property.Kind, value));
            }
            if (clip is TreeClip tree)
            {
                if (tree.ExecutionPhase != TimelineTreeExecutionPhase.Commit)
                    result.Add(new TimelineAuthoringPropertyValue("executionPhase", TimelineAuthoringPropertyKind.Enum, tree.ExecutionPhase));
                if (tree.ClipExitSource != TimelineClipExitSource.TreeDecision)
                    result.Add(new TimelineAuthoringPropertyValue("exitSource", TimelineAuthoringPropertyKind.Enum, tree.ClipExitSource));
                if (tree.AssetTree)
                    result.Add(new TimelineAuthoringPropertyValue("assetTree", TimelineAuthoringPropertyKind.Object, tree.AssetTree));
            }
            return result;
        }

        public static UnityEngine.Object ReferenceAsset(Clip clip) =>
            clip is TreeClip tree ? tree.AssetTree : TimelineAuthoringClipBinding.SourceAsset(clip);

        public static TimelineAuthoringReferenceValue Reference(Clip clip)
        {
            UnityEngine.Object asset = ReferenceAsset(clip);
            if (asset)
                return new TimelineAuthoringReferenceValue(asset);
            if (clip is ITimelineAuthoringClipSource source)
                return new TimelineAuthoringReferenceValue(null, source.SourceMissingErrorCode, source.SourceMissingError);
            if (clip is TreeClip)
                return new TimelineAuthoringReferenceValue(null, "timeline_tree_source_unsupported", "TreeClip不是正式资产节点图引用。");
            return new TimelineAuthoringReferenceValue(null);
        }

        public static OrchestrationTime DefaultEndTime(Clip clip, OrchestrationTime startTime,
            UnityEngine.Object referenceObject) =>
            clip is ITimelineAuthoringClipSource source && referenceObject
                ? startTime + OrchestrationTime.FromSeconds(source.GetSourceDuration(referenceObject))
                : startTime + (clip is SignalClip ? TimelineTimeGrid.Position(1, TimelineUtility.FrameRate) : OrchestrationTime.FromSeconds(0.05d));

        public static bool NeedsSegmentOverride(Clip clip, UnityEngine.Object referenceObject)
        {
            if (clip is TreeClip tree && tree.ClipExitSource == TimelineClipExitSource.TreeDecision)
                return false;
            return clip.EndTime != DefaultEndTime(clip, clip.StartTime, referenceObject) ||
                clip.SelfEaseInTime.RawValue != 0 || clip.SelfEaseOutTime.RawValue != 0 || clip.ClipInTime.RawValue != 0;
        }

        public static void Apply(TimelineData timeline, Clip clip,
            IReadOnlyList<TimelineAuthoringPropertyValue> values)
        {
            TimelineAuthoringClipConfiguration configuration = TimelineAuthoringClipBinding.Defaults(clip);
            TimelineTreeExecutionPhase executionPhase = TimelineTreeExecutionPhase.Commit;
            TimelineClipExitSource exitSource = TimelineClipExitSource.TreeDecision;
            UnityEngine.Object treeAsset = null;
            foreach (TimelineAuthoringPropertyValue value in values)
            {
                switch (value.PropertyId)
                {
                    case "executionPhase": executionPhase = (TimelineTreeExecutionPhase)value.Value; break;
                    case "exitSource": exitSource = (TimelineClipExitSource)value.Value; break;
                    case "assetTree": treeAsset = (UnityEngine.Object)value.Value; break;
                    default: configuration.Set(value.PropertyId, value.Value); break;
                }
            }
            TimelineAuthoringClipBinding.Configure(timeline, clip, configuration);
            if (clip is TreeClip tree)
            {
                if (treeAsset)
                    tree.SetAssetTree(treeAsset as ScriptableObject);
                tree.SetExecutionPhase(executionPhase);
                tree.SetExitSource(tree.ExecutionDomain == TimelineExecutionDomain.Logic ? TimelineClipExitSource.TreeDecision : exitSource);
            }
        }

        static bool ValuesEqual(object left, object right)
        {
            if (left is Vector2 vector && right is Vector2 other)
                return vector == other;
            return Equals(left, right);
        }
    }
}
#endif
