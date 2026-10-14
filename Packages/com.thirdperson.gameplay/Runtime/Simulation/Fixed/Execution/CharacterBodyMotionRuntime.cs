using BodyMotionIntegrationPlan = ThirdPersonSimulation.BodyMotionIntegrationPlan<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector3>;
using BodyMotionPrepareResult = ThirdPersonSimulation.BodyMotionPrepareResult<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector3, ThirdPersonSimulation.Fixed.CharacterMotionRequest>;
using ResolvedGameplayMotion = ThirdPersonSimulation.ResolvedGameplayMotion<ThirdPersonSimulation.Fixed.FixedScalar, ThirdPersonSimulation.Fixed.FixedVector2, ThirdPersonSimulation.Fixed.FixedVector3>;
using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.Fixed
{
    public static class CharacterBodyMotionRuntime
    {
        public static BodyMotionPrepareResult Prepare(
            ActorId actorId,
            SimulationTick tick,
            in WorldBodyState beforeBody,
            in ResolvedGameplayMotion gameplayMotion,
            in BodyMotionParameters<FixedScalar> parameters,
            FixedScalar tickDelta)
        {
            if (!actorId.IsValid || !tick.IsValid || beforeBody.ActorId != actorId)
                throw new ArgumentException("Body Motion Prepare identity is inconsistent.");
            if (tickDelta <= FixedScalar.Zero)
                throw new ArgumentOutOfRangeException(nameof(tickDelta));

            BodyMotionIntegrationPlan plan = BodyMotionIntegration<FixedScalar, FixedVector2, FixedVector3, FixedSimulationMath>.Prepare(
                actorId, tick, beforeBody.VerticalVelocity, gameplayMotion.Displacement, in parameters, tickDelta);
            FixedVector3 requestedDisplacement = plan.RequestedDisplacement;
            FixedVector3 requestedVelocity = default(FixedSimulationMath).DivideVector3(requestedDisplacement, tickDelta);
            var motion = new CharacterMotionRequest(
                "body-motion:integrated",
                requestedDisplacement,
                requestedVelocity,
                gameplayMotion.LocomotionPlanarBasis,
                gameplayMotion.YawDegrees,
                WorldMotionSpace.World,
                requestedDisplacement != FixedVector3.Zero || gameplayMotion.YawDegrees != FixedScalar.Zero,
                gameplayMotion.MovementPlaybackClock,
                gameplayMotion.LocomotionTimeline,
                gameplayMotion.ActionOwnerIdentity,
                gameplayMotion.GameplayResultOwnerIdentity);
            return new BodyMotionPrepareResult(plan, motion);
        }

        public static WorldBodyState Finalize(
            in WorldBodyState beforeBody,
            in BodyMotionIntegrationPlan plan,
            FixedVector3 finalPosition,
            FixedYaw finalYaw,
            FixedVector3 appliedDisplacement,
            bool grounded,
            WorldCollisionSummary collision,
            FixedScalar tickDelta)
        {
            RequirePlan(in beforeBody, in plan, tickDelta);
            FixedScalar verticalVelocity = BodyMotionIntegration<FixedScalar, FixedVector2, FixedVector3, FixedSimulationMath>.ResolveVerticalVelocity(
                plan.CandidateVerticalVelocity, grounded, collision);
            return new WorldBodyState(
                beforeBody.ActorId,
                finalPosition,
                finalYaw,
                default(FixedSimulationMath).DivideVector3(appliedDisplacement, tickDelta),
                verticalVelocity,
                grounded,
                collision);
        }

        static void RequirePlan(in WorldBodyState beforeBody, in BodyMotionIntegrationPlan plan, FixedScalar tickDelta)
        {
            if (beforeBody.ActorId != plan.ActorId || tickDelta <= FixedScalar.Zero || tickDelta != plan.TickDelta ||
                beforeBody.VerticalVelocity != plan.PreviousVerticalVelocity)
            {
                throw new InvalidOperationException("Body Motion integration plan does not match the World solve input.");
            }
        }

    }
}
