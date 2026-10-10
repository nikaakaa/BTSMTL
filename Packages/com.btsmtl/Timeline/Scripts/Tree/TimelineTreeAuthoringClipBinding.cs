#if UNITY_EDITOR
using System;
using ThirdPersonSimulation.Fixed;
using UnityEngine;

namespace BTSMTL.Timeline
{
    public sealed class TimelineTreeAuthoringClipExport
    {
        internal TimelineTreeAuthoringClipExport(ScriptableObject assetTree, string phase)
        {
            AssetTree = assetTree;
            Phase = phase ?? string.Empty;
        }

        public ScriptableObject AssetTree { get; }
        public string Phase { get; }
    }

    public static class TimelineTreeAuthoringClipBinding
    {
        public static Clip CreateClip(
            TimelineData timeline,
            TimelineContractCatalog catalog,
            Track track,
            ScriptableObject assetTree,
            FixedScalar startTime)
        {
            if (track is not TreeTrack tree)
                throw new ArgumentException("Timeline Tree authoring binding requires a TreeTrack.", nameof(track));
            return timeline.AddClip(catalog, assetTree, tree, startTime);
        }

        public static TimelineTreeAuthoringClipExport Export(Clip clip)
        {
            if (clip is not TreeClip value)
                throw new ArgumentException("Timeline Tree authoring binding requires a TreeClip.", nameof(clip));
            return new TimelineTreeAuthoringClipExport(value.AssetTree, value.ExecutionPhase.ToString());
        }

        public static void Apply(Clip clip, ScriptableObject tree, string phase)
        {
            if (clip is not TreeClip value)
                throw new ArgumentException("Timeline Tree authoring binding requires a TreeClip.", nameof(clip));
            if (tree != null)
                value.SetAssetTree(tree);
            value.SetExecutionPhase(Enum.Parse<TimelineTreeExecutionPhase>(
                phase ?? TimelineTreeExecutionPhase.Commit.ToString(), false));
        }
    }
}
#endif
