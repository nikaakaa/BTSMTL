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
            int locomotionContributionIndex,
            int actionContributionIndex,
            int gameplayResultContributionIndex)
        {
            Displacement = displacement;
            YawDegrees = yawDegrees;
            LocomotionPlanarBasis = locomotionPlanarBasis;
            HasMotion = hasMotion;
            LocomotionContributionIndex = locomotionContributionIndex;
            ActionContributionIndex = actionContributionIndex;
            GameplayResultContributionIndex = gameplayResultContributionIndex;
        }

        public TVector3 Displacement { get; }
        public TScalar YawDegrees { get; }
        public TVector2 LocomotionPlanarBasis { get; }
        public bool HasMotion { get; }
        public int LocomotionContributionIndex { get; }
        public int ActionContributionIndex { get; }
        public int GameplayResultContributionIndex { get; }
    }

    public readonly struct SimulationMotionContribution<TScalar, TVector2, TVector3, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
    {
        public SimulationMotionContribution(
            int sourceId,
            ulong sourceGeneration,
            TVector3 displacement,
            TScalar yawDegrees,
            TVector2 planarBasis,
            SimulationMotionContributionSpace space,
            TScalar weight,
            int priority,
            SimulationMotionChannel channel,
            SimulationMotionBlendMode blendMode,
            bool consumeLowerChannels)
        {
            SourceId = sourceId;
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
        }

        public int SourceId { get; }
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
        public bool HasDelta => default(TMath).GreaterThan(Weight, default(TMath).Zero) &&
            (!default(TMath).IsVector3Zero(Displacement) || !default(TMath).IsZero(YawDegrees));
        public bool ClaimsLowerChannels => default(TMath).GreaterThan(Weight, default(TMath).Zero) &&
            BlendMode == SimulationMotionBlendMode.Override &&
            ConsumeLowerChannels;
        public bool CanResolve => HasDelta || ClaimsLowerChannels || Channel == SimulationMotionChannel.Locomotion;
    }

    public struct ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath>
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
            int resolvedOwnerContributionIndex,
            int traceContributionIndex,
            TVector3 resolvedOwnerDisplacement,
            TScalar resolvedOwnerYawDegrees,
            int participatingSourceCount)
        {
            Channel = channel;
            Displacement = displacement;
            YawDegrees = yawDegrees;
            PlanarBasis = planarBasis;
            HasContribution = hasContribution;
            ClaimsLowerChannels = claimsLowerChannels;
            ResolvedOwnerContributionIndex = resolvedOwnerContributionIndex;
            TraceContributionIndex = traceContributionIndex;
            ResolvedOwnerDisplacement = resolvedOwnerDisplacement;
            ResolvedOwnerYawDegrees = resolvedOwnerYawDegrees;
            ParticipatingSourceCount = participatingSourceCount;
            MotionCorrectionDisplacement = default(TMath).ZeroVector3;
            MotionCorrectionYawDegrees = default(TMath).Zero;
        }

        public SimulationMotionChannel Channel { get; }
        public TVector3 Displacement { get; private set; }
        public TScalar YawDegrees { get; private set; }
        public TVector2 PlanarBasis { get; }
        public bool HasContribution { get; }
        public bool ClaimsLowerChannels { get; }
        public int ResolvedOwnerContributionIndex { get; }
        public int TraceContributionIndex { get; }
        public TVector3 ResolvedOwnerDisplacement { get; }
        public TScalar ResolvedOwnerYawDegrees { get; }
        public int ParticipatingSourceCount { get; }
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
