using System;

namespace ThirdPersonSimulation
{
    public readonly struct ResolvedGameplayMotion<TScalar, TVector2, TVector3>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
    {
        public ResolvedGameplayMotion(
            TVector3 displacement,
            TScalar yawDegrees,
            TVector2 locomotionPlanarBasis,
            bool hasMotion,
            CommittedMovementPlaybackClock movementPlaybackClock,
            CommittedLocomotionPlanarMotionTimeline locomotionTimeline,
            CanonicalTextValue actionOwnerIdentity,
            CanonicalTextValue gameplayResultOwnerIdentity)
        {
            Displacement = displacement;
            YawDegrees = yawDegrees;
            LocomotionPlanarBasis = locomotionPlanarBasis;
            HasMotion = hasMotion;
            MovementPlaybackClock = movementPlaybackClock;
            LocomotionTimeline = locomotionTimeline;
            ActionOwnerIdentity = actionOwnerIdentity;
            GameplayResultOwnerIdentity = gameplayResultOwnerIdentity;
        }

        public TVector3 Displacement { get; }
        public TScalar YawDegrees { get; }
        public TVector2 LocomotionPlanarBasis { get; }
        public bool HasMotion { get; }
        public CommittedMovementPlaybackClock MovementPlaybackClock { get; }
        public CommittedLocomotionPlanarMotionTimeline LocomotionTimeline { get; }
        public CanonicalTextValue ActionOwnerIdentity { get; }
        public CanonicalTextValue GameplayResultOwnerIdentity { get; }
    }

    internal enum SimulationMotionChannel
    {
        Locomotion = 0,
        Action = 1,
        GameplayResult = 2
    }

    internal enum SimulationMotionBlendMode
    {
        Additive = 0,
        WeightedBlend = 1,
        Override = 2
    }

    internal enum SimulationMotionContributionSpace
    {
        ActorLocal = 0,
        World = 1
    }

    internal readonly struct SimulationMotionContribution<TScalar, TVector2, TVector3, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
    {
        internal SimulationMotionContribution(CanonicalTextValue sourceIdentity,
            TVector3 displacement, TScalar yawDegrees, TScalar weight, int priority,
            SimulationMotionContributionSpace space, SimulationMotionChannel channel,
            SimulationMotionBlendMode blendMode, bool consumeLowerChannels)
        {
            Source = default;
            SourceIdentity = sourceIdentity;
            AbilityId = default;
            SourceGeneration = 0;
            Displacement = displacement;
            YawDegrees = yawDegrees;
            PlanarBasis = default;
            Space = space;
            Weight = weight;
            Priority = priority;
            Channel = channel;
            BlendMode = blendMode;
            ConsumeLowerChannels = consumeLowerChannels;
            MovementPlaybackClock = default;
            LocomotionTimeline = default;
        }

        public SimulationMotionContribution(
            SimulationExecutionSource source,
            CanonicalTextValue sourceIdentity,
            CharacterSkillId abilityId,
            ulong sourceGeneration,
            TVector3 displacement,
            TScalar yawDegrees,
            TVector2 planarBasis,
            SimulationMotionContributionSpace space,
            TScalar weight,
            int priority,
            SimulationMotionChannel channel,
            SimulationMotionBlendMode blendMode,
            bool consumeLowerChannels,
            CommittedMovementPlaybackClock movementPlaybackClock,
            CommittedLocomotionPlanarMotionTimeline locomotionTimeline)
        {
            if (!source.IsValid)
                throw new ArgumentException("Motion contribution source is invalid.", nameof(source));
            Source = source;
            SourceIdentity = CanonicalTextValue.IsNullOrEmpty(sourceIdentity)
                ? throw new ArgumentException("Motion contribution source identity is incomplete.", nameof(sourceIdentity))
                : sourceIdentity;
            AbilityId = abilityId;
            SourceGeneration = sourceGeneration;
            Displacement = displacement;
            YawDegrees = yawDegrees;
            PlanarBasis = planarBasis;
            Space = space;
            Weight = default(TMath).Clamp(weight, default(TMath).Zero, default(TMath).One);
            Priority = priority;
            Channel = channel;
            BlendMode = blendMode;
            ConsumeLowerChannels = consumeLowerChannels;
            if (channel == SimulationMotionChannel.Locomotion && !movementPlaybackClock.IsValid)
                throw new ArgumentException("Locomotion motion contribution has no committed Movement playback clock.", nameof(movementPlaybackClock));
            if (channel != SimulationMotionChannel.Locomotion && movementPlaybackClock.IsValid)
                throw new ArgumentException("Only the Locomotion motion channel may carry a committed Movement playback clock.", nameof(movementPlaybackClock));
            MovementPlaybackClock = movementPlaybackClock;
            if (channel == SimulationMotionChannel.Locomotion &&
                (!locomotionTimeline.IsValid || !locomotionTimeline.Matches(movementPlaybackClock)))
            {
                throw new ArgumentException("Locomotion motion contribution has no matching committed motion timeline.", nameof(locomotionTimeline));
            }
            if (channel != SimulationMotionChannel.Locomotion && locomotionTimeline.IsValid)
                throw new ArgumentException("Only the Locomotion motion channel may carry a committed motion timeline.", nameof(locomotionTimeline));
            LocomotionTimeline = locomotionTimeline;
        }

        public SimulationExecutionSource Source { get; }
        public CanonicalTextValue SourceIdentity { get; }
        public CharacterSkillId AbilityId { get; }
        public ulong SourceGeneration { get; }
        public TVector3 Displacement { get; }
        public TScalar YawDegrees { get; }
        public TVector2 PlanarBasis { get; }
        public SimulationMotionContributionSpace Space { get; }
        public TScalar Weight { get; }
        public int Priority { get; }
        public SimulationMotionChannel Channel { get; }
        public SimulationMotionBlendMode BlendMode { get; }
        public bool ConsumeLowerChannels { get; }
        public CommittedMovementPlaybackClock MovementPlaybackClock { get; }
        public CommittedLocomotionPlanarMotionTimeline LocomotionTimeline { get; }
        public bool HasDelta => default(TMath).GreaterThan(Weight, default(TMath).Zero) &&
            (!default(TMath).IsVector3Zero(Displacement) || !default(TMath).IsZero(YawDegrees));
        public bool ClaimsLowerChannels => default(TMath).GreaterThan(Weight, default(TMath).Zero) &&
            BlendMode == SimulationMotionBlendMode.Override &&
            ConsumeLowerChannels;
        public bool CanResolve => HasDelta || ClaimsLowerChannels || MovementPlaybackClock.IsValid;
        internal void Retain() { Source.Retain(); SourceIdentity.Retain(); }
        internal void Release() { Source.Release(); SourceIdentity.Release(); }
    }

    internal struct ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
    {
        public ResolvedMotionChannel(
            SimulationMotionChannel channel,
            TVector3 displacement,
            TScalar yawDegrees,
            TVector2 planarBasis,
            bool hasContribution,
            bool claimsLowerChannels,
            SimulationExecutionSource resolvedOwnerSource,
            CharacterSkillId resolvedOwnerAbilityId,
            CanonicalTextValue resolvedOwnerIdentity,
            CommittedMovementPlaybackClock movementPlaybackClock,
            CommittedLocomotionPlanarMotionTimeline locomotionTimeline,
            TVector3 resolvedOwnerDisplacement,
            TScalar resolvedOwnerYawDegrees,
            SimulationExecutionSource traceSource,
            CharacterSkillId traceAbilityId,
            ulong traceSourceGeneration,
            int participatingSourceCount,
            ulong participatingSourceFingerprint)
        {
            Channel = channel;
            Displacement = displacement;
            YawDegrees = yawDegrees;
            PlanarBasis = planarBasis;
            HasContribution = hasContribution;
            ClaimsLowerChannels = claimsLowerChannels;
            ResolvedOwnerSource = resolvedOwnerSource;
            ResolvedOwnerAbilityId = resolvedOwnerAbilityId;
            ResolvedOwnerIdentity = resolvedOwnerIdentity;
            MovementPlaybackClock = movementPlaybackClock;
            LocomotionTimeline = locomotionTimeline;
            ResolvedOwnerDisplacement = resolvedOwnerDisplacement;
            ResolvedOwnerYawDegrees = resolvedOwnerYawDegrees;
            TraceSource = traceSource;
            TraceAbilityId = traceAbilityId;
            TraceSourceGeneration = traceSourceGeneration;
            ParticipatingSourceCount = participatingSourceCount;
            ParticipatingSourceFingerprint = participatingSourceFingerprint;
            MotionCorrectionDisplacement = default(TMath).ZeroVector3;
            MotionCorrectionYawDegrees = default(TMath).Zero;
        }

        public SimulationMotionChannel Channel { get; }
        public TVector3 Displacement { get; private set; }
        public TScalar YawDegrees { get; private set; }
        public TVector2 PlanarBasis { get; }
        public bool HasContribution { get; }
        public bool ClaimsLowerChannels { get; }
        public SimulationExecutionSource ResolvedOwnerSource { get; }
        public CharacterSkillId ResolvedOwnerAbilityId { get; }
        public CanonicalTextValue ResolvedOwnerIdentity { get; }
        public CommittedMovementPlaybackClock MovementPlaybackClock { get; }
        public CommittedLocomotionPlanarMotionTimeline LocomotionTimeline { get; }
        public TVector3 ResolvedOwnerDisplacement { get; }
        public TScalar ResolvedOwnerYawDegrees { get; }
        public SimulationExecutionSource TraceSource { get; }
        public CharacterSkillId TraceAbilityId { get; }
        public ulong TraceSourceGeneration { get; }
        public int ParticipatingSourceCount { get; }
        public ulong ParticipatingSourceFingerprint { get; }
        public bool HasDelta => !default(TMath).IsVector3Zero(Displacement) || !default(TMath).IsZero(YawDegrees);

        public TVector3 MotionCorrectionDisplacement { get; private set; }
        public TScalar MotionCorrectionYawDegrees { get; private set; }

        public void ApplyMotionWarp(
            TVector3 sourceDisplacement, TScalar sourceYaw,
            TVector3 warpedDisplacement, TScalar warpedYaw)
        {
            Displacement = default(TMath).AddVector3(Displacement,
                default(TMath).SubtractVector3(warpedDisplacement, sourceDisplacement));
            YawDegrees = default(TMath).Add(YawDegrees, default(TMath).Subtract(warpedYaw, sourceYaw));
            MotionCorrectionDisplacement = default(TMath).AddVector3(MotionCorrectionDisplacement,
                default(TMath).SubtractVector3(warpedDisplacement, sourceDisplacement));
            MotionCorrectionYawDegrees = default(TMath).Add(MotionCorrectionYawDegrees,
                default(TMath).Subtract(warpedYaw, sourceYaw));
        }
    }

}
