using UnityEngine;
using System.Collections.Generic;
using System;

namespace ThirdPersonCamera
{
    public abstract class CameraEffectAsset : ScriptableObject
    {
        public abstract string EffectId { get; }
        public abstract int EffectPriority { get; }
    }
}

namespace BTSMTL.Timeline
{
    public interface ITimelineCameraEffectAsset
    {
        TimelineCameraResourceKind ResourceKind { get; }
    }

    public interface ITimelineCameraSourceAsset : ITimelineCameraEffectAsset
    {
        float SourceDuration { get; }
        string ContentRevision { get; }
        IReadOnlyList<TimelineCurveChannelDescriptor> CurveChannels { get; }
        event Action ContentChanged;
        void ReplaceCurves(IReadOnlyList<TimelineCurveChannelValue> curves);
    }
}
