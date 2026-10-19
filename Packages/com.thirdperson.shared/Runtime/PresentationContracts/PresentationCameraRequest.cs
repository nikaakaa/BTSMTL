using System;

namespace ThirdPersonSimulation
{
    public enum PresentationCameraRequestKind : byte
    {
        Sequence = 1,
        Effect = 2,
        Response = 3,
        Target = 4
    }

    public enum PresentationCameraRequestLifecycle : byte
    {
        Activate = 1,
        Retire = 2
    }

    public readonly partial struct PresentationCameraTargetTiming
    {
        public PresentationCameraTargetTiming(float delaySeconds, float durationSeconds, bool ignoreTimeScale)
        {
            DelaySeconds = delaySeconds;
            DurationSeconds = durationSeconds;
            IgnoreTimeScale = ignoreTimeScale;
            RequireRecord();
        }

        public float DelaySeconds { get; }
        public float DurationSeconds { get; }
        public bool IgnoreTimeScale { get; }

        [ValueValidation]
        void RequireRecord()
        {
            if (!float.IsFinite(DelaySeconds) || DelaySeconds < 0f)
                throw new ArgumentOutOfRangeException("DelaySeconds");
            if (!float.IsFinite(DurationSeconds) || DurationSeconds < 0f)
                throw new ArgumentOutOfRangeException("DurationSeconds");
        }
    }

    [ValueStorage(UseConstructor = true), ValueOperations]
    public readonly partial struct PresentationCameraRequest
    {
        PresentationCameraRequest(
            PresentationCameraRequestKind kind,
            PresentationCameraRequestLifecycle lifecycle,
            string requestId,
            string sequenceId,
            string resourceId,
            string targetKey,
            string anchorKey,
            string aimPointKey,
            string preferredBoneKey,
            int mode,
            int effectKind,
            int interruptPolicy,
            int priority,
            float weight,
            float blendInSeconds,
            float blendOutSeconds,
            float manualOrbitWeight,
            float pitchWeight,
            float yawWeight,
            bool captureLockStartOnSubmit,
            PresentationCameraTargetTiming followTiming = default,
            PresentationCameraTargetTiming aimTiming = default,
            string resourceRevision = default,
            float sourceStartTime = 0f,
            float sourceEndTime = 0f)
        {
            Kind = kind;
            Lifecycle = lifecycle;
            RequestId = Normalize(requestId);
            SequenceId = Normalize(sequenceId);
            ResourceId = Normalize(resourceId);
            ResourceRevision = Normalize(resourceRevision);
            SourceStartTime = sourceStartTime;
            SourceEndTime = sourceEndTime;
            TargetKey = targetKey == null ? string.Empty : targetKey.Trim();
            AnchorKey = Normalize(anchorKey);
            AimPointKey = Normalize(aimPointKey);
            PreferredBoneKey = Normalize(preferredBoneKey);
            Mode = mode;
            EffectKind = effectKind;
            InterruptPolicy = interruptPolicy;
            Priority = priority;
            Weight = weight;
            BlendInSeconds = blendInSeconds;
            BlendOutSeconds = blendOutSeconds;
            ManualOrbitWeight = manualOrbitWeight;
            PitchWeight = pitchWeight;
            YawWeight = yawWeight;
            CaptureLockStartOnSubmit = captureLockStartOnSubmit;
            FollowTiming = followTiming;
            AimTiming = aimTiming;
            RequireRecord();
        }

        public PresentationCameraRequestKind Kind { get; }
        public PresentationCameraRequestLifecycle Lifecycle { get; }
        [ValueTextRules(NullToEmpty = true)]
        public string RequestId { get; }
        [ValueTextRules(NullToEmpty = true)]
        public string SequenceId { get; }
        [ValueTextRules(NullToEmpty = true)]
        public string ResourceId { get; }
        [ValueTextRules(NullToEmpty = true)]
        public string ResourceRevision { get; }
        public float SourceStartTime { get; }
        public float SourceEndTime { get; }
        [ValueTextRules(NullToEmpty = true, Trim = true)]
        public string TargetKey { get; }
        [ValueTextRules(NullToEmpty = true)]
        public string AnchorKey { get; }
        [ValueTextRules(NullToEmpty = true)]
        public string AimPointKey { get; }
        [ValueTextRules(NullToEmpty = true)]
        public string PreferredBoneKey { get; }
        public int Mode { get; }
        public int EffectKind { get; }
        public int InterruptPolicy { get; }
        public int Priority { get; }
        public float Weight { get; }
        public float BlendInSeconds { get; }
        public float BlendOutSeconds { get; }
        public float ManualOrbitWeight { get; }
        public float PitchWeight { get; }
        public float YawWeight { get; }
        public bool CaptureLockStartOnSubmit { get; }
        public PresentationCameraTargetTiming FollowTiming { get; }
        public PresentationCameraTargetTiming AimTiming { get; }
        [ValuePresence]
        public bool IsValid => IsDefined(Kind) && IsDefined(Lifecycle);


        public PresentationCameraRequest WithActionTarget(string targetActorId) =>
            new PresentationCameraRequest(
                Kind, Lifecycle, RequestId, SequenceId, ResourceId,
                targetActorId, AnchorKey, AimPointKey, PreferredBoneKey, Mode, EffectKind,
                InterruptPolicy, Priority, Weight, BlendInSeconds, BlendOutSeconds,
                ManualOrbitWeight, PitchWeight, YawWeight, CaptureLockStartOnSubmit,
                FollowTiming, AimTiming, ResourceRevision, SourceStartTime, SourceEndTime);

        public static PresentationCameraRequest Sequence(
            PresentationCameraRequestLifecycle lifecycle,
            string sequenceId,
            int mode,
            int interruptPolicy,
            int priority,
            float weight,
            float blendInSeconds,
            float blendOutSeconds,
            string targetKey) =>
            new PresentationCameraRequest(
                PresentationCameraRequestKind.Sequence,
                lifecycle,
                sequenceId,
                sequenceId,
                string.Empty,
                targetKey,
                string.Empty,
                string.Empty,
                string.Empty,
                mode,
                0,
                interruptPolicy,
                priority,
                weight,
                blendInSeconds,
                blendOutSeconds,
                1f,
                1f,
                1f,
                true);

        public static PresentationCameraRequest Effect(
            PresentationCameraRequestLifecycle lifecycle,
            string requestId,
            int effectKind,
            string resourceId,
            int priority,
            float weight,
            bool captureLockStartOnSubmit,
            string resourceRevision = default,
            float sourceStartTime = 0f,
            float sourceEndTime = 0f) =>
            new PresentationCameraRequest(
                PresentationCameraRequestKind.Effect,
                lifecycle,
                requestId,
                string.Empty,
                resourceId,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                0,
                effectKind,
                0,
                priority,
                weight,
                0f,
                0f,
                1f,
                1f,
                1f,
                captureLockStartOnSubmit,
                resourceRevision: resourceRevision, sourceStartTime: sourceStartTime, sourceEndTime: sourceEndTime);

        public static PresentationCameraRequest Response(
            PresentationCameraRequestLifecycle lifecycle,
            int mode,
            float manualOrbitWeight,
            float pitchWeight,
            float yawWeight,
            int priority,
            float weight) =>
            new PresentationCameraRequest(
                PresentationCameraRequestKind.Response,
                lifecycle,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                mode,
                0,
                0,
                priority,
                weight,
                0f,
                0f,
                manualOrbitWeight,
                pitchWeight,
                yawWeight,
                true);

        public static PresentationCameraRequest Target(
            PresentationCameraRequestLifecycle lifecycle,
            string targetKey,
            string anchorKey,
            string aimPointKey,
            string preferredBoneKey,
            int priority,
            float weight,
            PresentationCameraTargetTiming followTiming,
            PresentationCameraTargetTiming aimTiming) =>
            new PresentationCameraRequest(
                PresentationCameraRequestKind.Target,
                lifecycle,
                string.Empty,
                string.Empty,
                string.Empty,
                targetKey,
                anchorKey,
                aimPointKey,
                preferredBoneKey,
                0,
                0,
                0,
                priority,
                weight,
                0f,
                0f,
                1f,
                1f,
                1f,
                true,
                followTiming,
                aimTiming);

        static string Normalize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (!value.AsSpan().Trim().SequenceEqual(value.AsSpan()))
                throw new ArgumentException("Camera request identity must be trimmed.");
            return value;
        }

        static bool IsDefined(PresentationCameraRequestKind kind) =>
            kind >= PresentationCameraRequestKind.Sequence &&
            kind <= PresentationCameraRequestKind.Target;

        static bool IsDefined(PresentationCameraRequestLifecycle lifecycle) =>
            lifecycle >= PresentationCameraRequestLifecycle.Activate &&
            lifecycle <= PresentationCameraRequestLifecycle.Retire;

        static float RequireFiniteNonNegative(float value, string name)
        {
            if (!float.IsFinite(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }

        static float RequireUnit(float value, string name)
        {
            if (!float.IsFinite(value) || value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(name);
            return value;
        }

        [ValueValidation]
        void RequireRecord()
        {
            if (!IsDefined(Kind))
                throw new ArgumentOutOfRangeException("Kind");
            if (!IsDefined(Lifecycle))
                throw new ArgumentOutOfRangeException("Lifecycle");
            RequireFiniteNonNegative(Weight, nameof(Weight));
            RequireFiniteNonNegative(BlendInSeconds, nameof(BlendInSeconds));
            RequireFiniteNonNegative(BlendOutSeconds, nameof(BlendOutSeconds));
            RequireUnit(ManualOrbitWeight, nameof(ManualOrbitWeight));
            RequireUnit(PitchWeight, nameof(PitchWeight));
            RequireUnit(YawWeight, nameof(YawWeight));
            Normalize(RequestId);
            Normalize(SequenceId);
            Normalize(ResourceId);
            Normalize(AnchorKey);
            Normalize(AimPointKey);
            Normalize(PreferredBoneKey);
        }
    }
}
