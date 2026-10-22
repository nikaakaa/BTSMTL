namespace ThirdPersonSimulation
{
    public enum ProgramMotionWarpPositionAxes : byte
    {
        Planar = 0,
        Full3D = 1
    }

    public enum ProgramMotionWarpTranslationMode : byte
    {
        Disabled = 0,
        ScaleToTarget = 1,
        SkewToTarget = 2,
        LinearToTarget = 3
    }

    public enum ProgramMotionWarpTargetOffsetSpace : byte
    {
        TargetLocal = 0,
        ApproachDirection = 1,
        ActorStartLocal = 2,
        World = 3
    }

    public enum ProgramMotionWarpRotationMode : byte
    {
        Disabled = 0,
        FaceTarget = 1,
        MatchTargetYaw = 2
    }

    public enum ProgramMotionWarpRotationMethod : byte
    {
        ProgressCurve = 0,
        ConstantRate = 1,
        ScaleSourceYaw = 2,
        TargetResponse = 3
    }

    public enum ProgramMotionWarpLimitPolicy : byte
    {
        ApplyClamped = 0,
        PreserveSource = 1
    }

    public enum ProgramMotionWarpLimitResult : byte
    {
        Applied = 0,
        AppliedClamped = 1,
        PreservedByLimitPolicy = 2
    }

    public enum MotionWarpWindowDecision : byte
    {
        Accepted = 1,
        Disabled = 2,
        NoTarget = 3,
        PreservedByLimitPolicy = 4
    }

    public static class MotionModifierDiagnosticCode
    {
        public const string SourceNotResolved = "motion_warp_source_not_resolved";
        public const string Disabled = "motion_warp_disabled";
        public const string WindowEntered = "motion_warp_window_entered";
        public const string TargetSnapshotRequired = "motion_warp_target_snapshot_required";
        public const string NoTargetByOptionalPolicy = "motion_warp_no_target_by_optional_policy";
        public const string AmbiguousModifier = "motion_warp_ambiguous_modifier";
        public const string InvalidState = "motion_warp_invalid_state";
        public const string FaceTargetZeroDirection = "motion_warp_face_target_zero_direction";
        public const string ApproachDirectionZero = "motion_warp_approach_direction_zero";
        public const string ScaleSourcePositionZero = "motion_warp_scale_source_position_zero";
        public const string ScaleSourceYawZero = "motion_warp_scale_source_yaw_zero";
        public const string TargetResolved = "motion_warp_target_resolved";
        public const string Applied = "motion_warp_applied";
        public const string AppliedClamped = "motion_warp_applied_clamped";
        public const string PreservedByLimitPolicy = "motion_warp_preserved_by_limit_policy";
    }
}
