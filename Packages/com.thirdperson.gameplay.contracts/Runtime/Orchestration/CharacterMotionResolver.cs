using System;

namespace ThirdPersonSimulation
{
    public static class CharacterMotionResolver<TScalar, TVector2, TVector3, TYaw, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TYaw : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>, IPlanarRotation<TVector3, TYaw>
    {
        public static ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath> ResolveChannel(
            ReadOnlySpan<SimulationMotionContribution<TScalar, TVector2, TVector3, TMath>> contributions,
            TYaw bodyYaw,
            SimulationMotionChannel channel)
        {
            TVector3 additiveDisplacement = default(TMath).ZeroVector3;
            TScalar additiveYaw = default(TMath).Zero;
            TVector3 weightedDisplacement = default(TMath).ZeroVector3;
            TScalar weightedYaw = default(TMath).Zero;
            TScalar totalWeight = default(TMath).Zero;
            SimulationMotionContribution<TScalar, TVector2, TVector3, TMath> overrideWinner = default;
            int overrideWinnerIndex = -1;
            TVector3 overrideDisplacement = default(TMath).ZeroVector3;
            TScalar overrideYaw = default(TMath).Zero;
            int traceContributionIndex = -1;
            int sourceCount = 0;
            bool hasAdditive = false;
            bool hasWeighted = false;
            bool hasOverride = false;
            for (int i = 0; i < contributions.Length; i++)
            {
                ref readonly SimulationMotionContribution<TScalar, TVector2, TVector3, TMath> contribution = ref contributions[i];
                if (contribution.Channel != channel || !contribution.CanResolve)
                    continue;
                if (traceContributionIndex < 0)
                    traceContributionIndex = i;
                sourceCount++;
                TVector3 resolved = contribution.Space == SimulationMotionContributionSpace.ActorLocal
                    ? default(TMath).RotatePlanar(contribution.Displacement, bodyYaw)
                    : contribution.Displacement;
                switch (contribution.BlendMode)
                {
                    case SimulationMotionBlendMode.Additive:
                        additiveDisplacement = default(TMath).AddVector3(additiveDisplacement,
                            default(TMath).MultiplyVector3(resolved, contribution.Weight));
                        additiveYaw = default(TMath).Add(additiveYaw,
                            default(TMath).Multiply(contribution.YawDegrees, contribution.Weight));
                        hasAdditive = true;
                        break;
                    case SimulationMotionBlendMode.WeightedBlend:
                        weightedDisplacement = default(TMath).AddVector3(weightedDisplacement,
                            default(TMath).MultiplyVector3(resolved, contribution.Weight));
                        weightedYaw = default(TMath).Add(weightedYaw,
                            default(TMath).Multiply(contribution.YawDegrees, contribution.Weight));
                        totalWeight = default(TMath).Add(totalWeight, contribution.Weight);
                        hasWeighted = true;
                        break;
                    case SimulationMotionBlendMode.Override:
                        if (!hasOverride || contribution.Priority > overrideWinner.Priority)
                        {
                            overrideWinner = contribution;
                            overrideWinnerIndex = i;
                            overrideDisplacement = default(TMath).MultiplyVector3(resolved, contribution.Weight);
                            overrideYaw = default(TMath).Multiply(contribution.YawDegrees, contribution.Weight);
                            hasOverride = true;
                        }
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Motion contribution source {contribution.SourceId} has invalid blend mode '{contribution.BlendMode}'.");
                }
            }
            if (!hasAdditive && !hasWeighted && !hasOverride)
                return new ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath>(channel,
                    default(TMath).ZeroVector3, default(TMath).Zero, default(TMath).ZeroVector2,
                    false, false, -1, -1, default(TMath).ZeroVector3, default(TMath).Zero, 0);

            TVector3 channelDisplacement = additiveDisplacement;
            TScalar channelYaw = additiveYaw;
            if (hasOverride)
            {
                channelDisplacement = default(TMath).AddVector3(channelDisplacement, overrideDisplacement);
                channelYaw = default(TMath).Add(channelYaw, overrideYaw);
            }
            else if (hasWeighted && default(TMath).GreaterThan(totalWeight, default(TMath).Zero))
            {
                channelDisplacement = default(TMath).AddVector3(channelDisplacement,
                    default(TMath).DivideVector3(weightedDisplacement, totalWeight));
                channelYaw = default(TMath).Add(channelYaw, default(TMath).Divide(weightedYaw, totalWeight));
            }
            if (channel == SimulationMotionChannel.Locomotion && !hasOverride)
                throw new InvalidOperationException("Resolved Locomotion motion has no single committed Movement playback clock owner.");
            var result = new ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath>(
                channel,
                channelDisplacement,
                channelYaw,
                hasOverride ? overrideWinner.PlanarBasis : default(TMath).ZeroVector2,
                true,
                hasOverride && overrideWinner.ConsumeLowerChannels,
                overrideWinnerIndex,
                hasOverride ? overrideWinnerIndex : traceContributionIndex,
                hasOverride ? overrideDisplacement : default(TMath).ZeroVector3,
                hasOverride ? overrideYaw : default(TMath).Zero,
                sourceCount);
            return result;
        }

        public static ulong ComputeParticipatingSourceFingerprint(
            in ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath> resolved,
            ReadOnlySpan<SimulationMotionContribution<TScalar, TVector2, TVector3, TMath>> contributions,
            ReadOnlySpan<CanonicalTextValue> sourceIdentitiesBySourceId)
        {
            if (!resolved.HasContribution)
                return 0;
            ulong fingerprint = 1469598103934665603UL;
            for (int index = 0; index < contributions.Length; index++)
            {
                ref readonly SimulationMotionContribution<TScalar, TVector2, TVector3, TMath> contribution = ref contributions[index];
                if (contribution.Channel == resolved.Channel && contribution.CanResolve)
                    fingerprint = MixSource(fingerprint, sourceIdentitiesBySourceId[contribution.SourceId].Span);
            }
            return fingerprint;
        }

        public static ResolvedGameplayMotion<TScalar, TVector2, TVector3> Compose(
            in ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath> locomotion,
            in ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath> action,
            in ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath> gameplayResult)
        {
            TVector3 displacement = default(TMath).ZeroVector3;
            TScalar yaw = default(TMath).Zero;
            ComposeChannel(locomotion, ref displacement, ref yaw);
            ComposeChannel(action, ref displacement, ref yaw);
            ComposeChannel(gameplayResult, ref displacement, ref yaw);
            return new ResolvedGameplayMotion<TScalar, TVector2, TVector3>(
                displacement,
                yaw,
                locomotion.PlanarBasis,
                !default(TMath).IsVector3Zero(displacement) || !default(TMath).IsZero(yaw),
                locomotion.ResolvedOwnerContributionIndex,
                action.ResolvedOwnerContributionIndex,
                gameplayResult.ResolvedOwnerContributionIndex);
        }

        static void ComposeChannel(
            in ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath> channel,
            ref TVector3 displacement,
            ref TScalar yaw)
        {
            if (!channel.HasContribution)
                return;
            if (channel.ClaimsLowerChannels)
            {
                displacement = channel.Displacement;
                yaw = channel.YawDegrees;
                return;
            }
            displacement = default(TMath).AddVector3(displacement, channel.Displacement);
            yaw = default(TMath).Add(yaw, channel.YawDegrees);
        }

        static ulong MixSource(ulong hash, ReadOnlySpan<char> identity)
        {
            unchecked
            {
                for (int i = 0; i < identity.Length; i++)
                {
                    hash ^= identity[i];
                    hash *= 1099511628211UL;
                }
                return hash;
            }
        }
    }

}
