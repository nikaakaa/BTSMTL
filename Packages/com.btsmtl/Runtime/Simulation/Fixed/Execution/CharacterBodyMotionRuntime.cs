using ThirdPersonSimulation;
using System;

namespace ThirdPersonSimulation.Fixed
{
    public readonly struct BodyMotionIntegrationPlan
    {
        internal BodyMotionIntegrationPlan(
            ActorId actorId,
            SimulationTick tick,
            FixedScalar tickDelta,
            FixedScalar previousVerticalVelocity,
            FixedScalar candidateVerticalVelocity,
            FixedVector3 requestedDisplacement)
        {
            if (!actorId.IsValid || !tick.IsValid || tickDelta <= FixedScalar.Zero)
            {
                throw new ArgumentException("Body Motion integration plan is incomplete.");
            }
            ActorId = actorId;
            Tick = tick;
            TickDelta = tickDelta;
            PreviousVerticalVelocity = previousVerticalVelocity;
            CandidateVerticalVelocity = candidateVerticalVelocity;
            RequestedDisplacement = requestedDisplacement;
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public FixedScalar TickDelta { get; }
        public FixedScalar PreviousVerticalVelocity { get; }
        public FixedScalar CandidateVerticalVelocity { get; }
        public FixedVector3 RequestedDisplacement { get; }
    }

    public readonly struct BodyMotionPrepareResult
    {
        internal BodyMotionPrepareResult(BodyMotionIntegrationPlan plan, CharacterMotionRequest motion)
        {
            Plan = plan;
            Motion = motion;
        }

        public BodyMotionIntegrationPlan Plan { get; }
        public CharacterMotionRequest Motion { get; }
    }

    public static class CharacterBodyMotionRuntime
    {
        public static BodyMotionPrepareResult Prepare(
            ActorId actorId,
            SimulationTick tick,
            WorldBodyState beforeBody,
            ResolvedGameplayMotion gameplayMotion,
            CharacterBodyMotionBinding binding,
            FixedScalar tickDelta)
        {
            if (!actorId.IsValid || !tick.IsValid || beforeBody.ActorId != actorId)
                throw new ArgumentException("Body Motion Prepare identity is inconsistent.");
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            if (tickDelta <= FixedScalar.Zero)
                throw new ArgumentOutOfRangeException(nameof(tickDelta));

            FixedScalar previous = beforeBody.VerticalVelocity;
            FixedScalar candidate = FixedScalar.Max(
                previous + FixedScalar.FromDouble(binding.GravityAcceleration) * tickDelta,
                -FixedScalar.FromDouble(binding.MaximumFallSpeed));
            FixedScalar gravityDisplacement = candidate * tickDelta;
            FixedVector3 gameplayDisplacement = gameplayMotion.Displacement;
            var requestedDisplacement = new FixedVector3(
                gameplayDisplacement.X,
                gameplayDisplacement.Y + gravityDisplacement,
                gameplayDisplacement.Z);
            var requestedVelocity = new FixedVector3(
                requestedDisplacement.X / tickDelta,
                requestedDisplacement.Y / tickDelta,
                requestedDisplacement.Z / tickDelta);
            var plan = new BodyMotionIntegrationPlan(
                actorId,
                tick,
                tickDelta,
                previous,
                candidate,
                requestedDisplacement);
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
            WorldBodyState beforeBody,
            BodyMotionIntegrationPlan plan,
            FixedVector3 finalPosition,
            FixedYaw finalYaw,
            FixedVector3 appliedDisplacement,
            bool grounded,
            WorldCollisionSummary collision,
            FixedScalar tickDelta)
        {
            RequirePlan(beforeBody, plan, tickDelta);
            FixedScalar verticalVelocity = plan.CandidateVerticalVelocity;
            if (verticalVelocity < FixedScalar.Zero && grounded)
                verticalVelocity = FixedScalar.Zero;
            else if (verticalVelocity > FixedScalar.Zero && (collision & WorldCollisionSummary.Above) != 0)
                verticalVelocity = FixedScalar.Zero;
            return new WorldBodyState(
                beforeBody.ActorId,
                finalPosition,
                finalYaw,
                new FixedVector3(
                    appliedDisplacement.X / tickDelta,
                    appliedDisplacement.Y / tickDelta,
                    appliedDisplacement.Z / tickDelta),
                verticalVelocity,
                grounded,
                collision);
        }

        static void RequirePlan(WorldBodyState beforeBody, BodyMotionIntegrationPlan plan, FixedScalar tickDelta)
        {
            if (beforeBody.ActorId != plan.ActorId || tickDelta <= FixedScalar.Zero || tickDelta != plan.TickDelta ||
                beforeBody.VerticalVelocity != plan.PreviousVerticalVelocity)
            {
                throw new InvalidOperationException("Body Motion integration plan does not match the World solve input.");
            }
        }

    }
}
