using ThirdPersonSimulation.QuantumNumerics;
using Photon.Deterministic;
using BodyMotionIntegrationPlan = ThirdPersonSimulation.BodyMotionIntegrationPlan<Photon.Deterministic.FP, Photon.Deterministic.FPVector3>;
using BodyMotionPrepareResult = ThirdPersonSimulation.BodyMotionPrepareResult<Photon.Deterministic.FP, Photon.Deterministic.FPVector3, ThirdPersonSimulation.FPGameplay.CharacterMotionRequest>;
using ResolvedGameplayMotion = ThirdPersonSimulation.ResolvedGameplayMotion<Photon.Deterministic.FP, Photon.Deterministic.FPVector2, Photon.Deterministic.FPVector3>;
using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.FPGameplay
{
    public static class CharacterBodyMotionRuntime
    {
        public static BodyMotionPrepareResult Prepare(
            ActorId actorId,
            SimulationTick tick,
            in WorldBodyState beforeBody,
            in ResolvedGameplayMotion gameplayMotion,
            in BodyMotionParameters<FP> parameters,
            FP tickDelta)
        {
            if (!actorId.IsValid || !tick.IsValid || beforeBody.ActorId != actorId)
                throw new ArgumentException("Body Motion Prepare identity is inconsistent.");
            if (tickDelta <= FP._0)
                throw new ArgumentOutOfRangeException(nameof(tickDelta));

            BodyMotionIntegrationPlan plan = BodyMotionIntegration<FP, FPVector2, FPVector3, QuantumSimulationMath>.Prepare(
                actorId, tick, beforeBody.VerticalVelocity, gameplayMotion.Displacement, in parameters, tickDelta);
            FPVector3 requestedDisplacement = plan.RequestedDisplacement;
            FPVector3 requestedVelocity = default(QuantumSimulationMath).DivideVector3(requestedDisplacement, tickDelta);
            var motion = new CharacterMotionRequest(
                "body-motion:integrated",
                requestedDisplacement,
                requestedVelocity,
                gameplayMotion.LocomotionPlanarBasis,
                gameplayMotion.YawDegrees,
                WorldMotionSpace.World,
                requestedDisplacement != FPVector3.Zero || gameplayMotion.YawDegrees != FP._0,
                gameplayMotion.MovementPlaybackClock,
                gameplayMotion.LocomotionTimeline,
                gameplayMotion.ActionOwnerIdentity,
                gameplayMotion.GameplayResultOwnerIdentity);
            return new BodyMotionPrepareResult(plan, motion);
        }

        public static WorldBodyState Finalize(
            in WorldBodyState beforeBody,
            in BodyMotionIntegrationPlan plan,
            FPVector3 finalPosition,
            FP finalYaw,
            FPVector3 appliedDisplacement,
            bool grounded,
            WorldCollisionSummary collision,
            FP tickDelta)
        {
            RequirePlan(in beforeBody, in plan, tickDelta);
            FP verticalVelocity = BodyMotionIntegration<FP, FPVector2, FPVector3, QuantumSimulationMath>.ResolveVerticalVelocity(
                plan.CandidateVerticalVelocity, grounded, collision);
            return new WorldBodyState(
                beforeBody.ActorId,
                finalPosition,
                finalYaw,
                default(QuantumSimulationMath).DivideVector3(appliedDisplacement, tickDelta),
                verticalVelocity,
                grounded,
                collision);
        }

        static void RequirePlan(in WorldBodyState beforeBody, in BodyMotionIntegrationPlan plan, FP tickDelta)
        {
            if (beforeBody.ActorId != plan.ActorId || tickDelta <= FP._0 || tickDelta != plan.TickDelta ||
                beforeBody.VerticalVelocity != plan.PreviousVerticalVelocity)
            {
                throw new InvalidOperationException("Body Motion integration plan does not match the World solve input.");
            }
        }

    }
}
