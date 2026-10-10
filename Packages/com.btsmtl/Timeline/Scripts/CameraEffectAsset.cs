using UnityEngine;

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
}
