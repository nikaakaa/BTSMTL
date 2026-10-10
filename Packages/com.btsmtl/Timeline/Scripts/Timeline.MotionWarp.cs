
namespace BTSMTL.Timeline
{
    public enum MotionWarpTranslationMode : byte
    {
        Disabled = 0,
        ScaleToTarget = 1,
        SkewToTarget = 2,
        LinearToTarget = 3
    }

    public enum MotionWarpTargetOffsetSpace : byte
    {
        TargetLocal = 0,
        ApproachDirection = 1,
        ActorStartLocal = 2,
        World = 3
    }

    public enum MotionWarpRotationMode : byte
    {
        Disabled = 0,
        FaceTarget = 1,
        MatchTargetYaw = 2
    }

    public enum MotionWarpRotationMethod : byte
    {
        ProgressCurve = 0,
        ConstantRate = 1,
        ScaleSourceYaw = 2,
        TargetResponse = 3
    }

    public enum MotionWarpLimitPolicy : byte
    {
        ApplyClamped = 0,
        PreserveSource = 1
    }

}
