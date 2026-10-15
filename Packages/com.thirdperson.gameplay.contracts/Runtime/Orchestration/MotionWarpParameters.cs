namespace ThirdPersonSimulation
{
    public readonly struct MotionWarpParameters<TScalar> where TScalar : struct
    {
        public MotionWarpParameters(
            ProgramMotionWarpTranslationMode translationMode,
            ProgramMotionWarpTargetOffsetSpace targetOffsetSpace,
            ProgramMotionWarpRotationMode rotationMode,
            ProgramMotionWarpRotationMethod rotationMethod,
            ProgramMotionWarpLimitPolicy limitPolicy,
            TScalar offsetX, TScalar offsetZ, TScalar yawOffset,
            TScalar maximumPosition, TScalar maximumYaw, TScalar maximumYawRate,
            TScalar scaleEpsilon)
        {
            TranslationMode = translationMode;
            TargetOffsetSpace = targetOffsetSpace;
            RotationMode = rotationMode;
            RotationMethod = rotationMethod;
            LimitPolicy = limitPolicy;
            OffsetX = offsetX;
            OffsetZ = offsetZ;
            YawOffset = yawOffset;
            MaximumPosition = maximumPosition;
            MaximumYaw = maximumYaw;
            MaximumYawRate = maximumYawRate;
            ScaleEpsilon = scaleEpsilon;
        }

        public ProgramMotionWarpTranslationMode TranslationMode { get; }
        public ProgramMotionWarpTargetOffsetSpace TargetOffsetSpace { get; }
        public ProgramMotionWarpRotationMode RotationMode { get; }
        public ProgramMotionWarpRotationMethod RotationMethod { get; }
        public ProgramMotionWarpLimitPolicy LimitPolicy { get; }
        public TScalar OffsetX { get; }
        public TScalar OffsetZ { get; }
        public TScalar YawOffset { get; }
        public TScalar MaximumPosition { get; }
        public TScalar MaximumYaw { get; }
        public TScalar MaximumYawRate { get; }
        public TScalar ScaleEpsilon { get; }
    }
}
