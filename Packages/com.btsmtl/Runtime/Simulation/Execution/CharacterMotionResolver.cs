using System;

namespace ThirdPersonSimulation
{
    internal static class CharacterMotionResolver<TScalar, TVector2, TVector3, TYaw, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TYaw : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>, IPlanarRotation<TVector3, TYaw>
    {
        public static ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath> ResolveChannel(
            SimulationMotionContribution<TScalar, TVector2, TVector3, TMath>[] contributions,
            int contributionCount,
            TYaw bodyYaw,
            SimulationMotionChannel channel)
        {
            TVector3 additiveDisplacement = default(TMath).ZeroVector3;
            TScalar additiveYaw = default(TMath).Zero;
            TVector3 weightedDisplacement = default(TMath).ZeroVector3;
            TScalar weightedYaw = default(TMath).Zero;
            TScalar totalWeight = default(TMath).Zero;
            SimulationMotionContribution<TScalar, TVector2, TVector3, TMath> overrideWinner = default;
            TVector3 overrideDisplacement = default(TMath).ZeroVector3;
            TScalar overrideYaw = default(TMath).Zero;
            SimulationExecutionSource traceSource = default;
            CharacterSkillId traceAbilityId = default;
            ulong traceSourceGeneration = 0;
            int sourceCount = 0;
            ulong sourceFingerprint = 1469598103934665603UL;
            bool hasAdditive = false;
            bool hasWeighted = false;
            bool hasOverride = false;
            for (int i = 0; i < contributionCount; i++)
            {
                ref readonly SimulationMotionContribution<TScalar, TVector2, TVector3, TMath> contribution = ref contributions[i];
                if (contribution.Channel != channel || !contribution.CanResolve)
                    continue;
                if (!traceSource.IsValid)
                {
                    traceSource = contribution.Source;
                    traceAbilityId = contribution.AbilityId;
                    traceSourceGeneration = contribution.SourceGeneration;
                }
                sourceCount++;
                sourceFingerprint = MixSource(sourceFingerprint, contribution.SourceIdentity.Span);
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
                            overrideDisplacement = default(TMath).MultiplyVector3(resolved, contribution.Weight);
                            overrideYaw = default(TMath).Multiply(contribution.YawDegrees, contribution.Weight);
                            hasOverride = true;
                        }
                        break;
                    default:
                        throw new InvalidOperationException(
                            $"Motion contribution '{contribution.SourceIdentity}' has invalid blend mode '{contribution.BlendMode}'.");
                }
            }
            if (!hasAdditive && !hasWeighted && !hasOverride)
                return new ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath>(channel, default(TMath).ZeroVector3, default(TMath).Zero, default(TMath).ZeroVector2, false, false, default, default, string.Empty, default, default, default(TMath).ZeroVector3, default(TMath).Zero, default, default, 0, 0, 0);

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
            CommittedMovementPlaybackClock movementPlaybackClock =
                channel == SimulationMotionChannel.Locomotion && hasOverride
                    ? overrideWinner.MovementPlaybackClock
                    : default;
            if (channel == SimulationMotionChannel.Locomotion && !movementPlaybackClock.IsValid)
                throw new InvalidOperationException("Resolved Locomotion motion has no single committed Movement playback clock owner.");
            var result = new ResolvedMotionChannel<TScalar, TVector2, TVector3, TMath>(
                channel,
                channelDisplacement,
                channelYaw,
                hasOverride ? overrideWinner.PlanarBasis : default(TMath).ZeroVector2,
                true,
                hasOverride && overrideWinner.ConsumeLowerChannels,
                hasOverride ? overrideWinner.Source : default,
                hasOverride ? overrideWinner.AbilityId : default,
                hasOverride ? overrideWinner.SourceIdentity : string.Empty,
                movementPlaybackClock,
                channel == SimulationMotionChannel.Locomotion && hasOverride
                    ? overrideWinner.LocomotionTimeline
                    : default,
                hasOverride ? overrideDisplacement : default(TMath).ZeroVector3,
                hasOverride ? overrideYaw : default(TMath).Zero,
                hasOverride ? overrideWinner.Source : traceSource,
                hasOverride ? overrideWinner.AbilityId : traceAbilityId,
                hasOverride ? overrideWinner.SourceGeneration : traceSourceGeneration,
                sourceCount,
                sourceFingerprint);
            return result;
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
                locomotion.MovementPlaybackClock,
                locomotion.LocomotionTimeline,
                action.ResolvedOwnerIdentity,
                gameplayResult.ResolvedOwnerIdentity);
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
