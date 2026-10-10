using System;

namespace ThirdPersonSimulation
{
    public readonly struct BodyMotionIntegrationPlan
    {
        internal BodyMotionIntegrationPlan(
            ActorId actorId,
            SimulationTick tick,
            Float32Scalar tickDelta,
            Float32Scalar previousVerticalVelocity,
            Float32Scalar candidateVerticalVelocity,
            Float32Vector3 requestedDisplacement)
        {
            if (!actorId.IsValid || !tick.IsValid || tickDelta <= Float32Scalar.Zero)
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
        public Float32Scalar TickDelta { get; }
        public Float32Scalar PreviousVerticalVelocity { get; }
        public Float32Scalar CandidateVerticalVelocity { get; }
        public Float32Vector3 RequestedDisplacement { get; }
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
            Float32Scalar tickDelta)
        {
            if (!actorId.IsValid || !tick.IsValid || beforeBody.ActorId != actorId)
                throw new ArgumentException("Body Motion Prepare identity is inconsistent.");
            if (binding == null)
                throw new ArgumentNullException(nameof(binding));
            if (tickDelta <= Float32Scalar.Zero)
                throw new ArgumentOutOfRangeException(nameof(tickDelta));

            Float32Scalar previous = beforeBody.VerticalVelocity;
            Float32Scalar candidate = Float32Scalar.Max(
                previous + Float32Scalar.FromDouble(binding.GravityAcceleration) * tickDelta,
                -Float32Scalar.FromDouble(binding.MaximumFallSpeed));
            Float32Scalar gravityDisplacement = candidate * tickDelta;
            Float32Vector3 gameplayDisplacement = gameplayMotion.Displacement;
            var requestedDisplacement = new Float32Vector3(
                gameplayDisplacement.X,
                gameplayDisplacement.Y + gravityDisplacement,
                gameplayDisplacement.Z);
            var requestedVelocity = new Float32Vector3(
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
                requestedDisplacement != Float32Vector3.Zero || gameplayMotion.YawDegrees != Float32Scalar.Zero,
                gameplayMotion.MovementPlaybackClock,
                gameplayMotion.LocomotionTimeline,
                gameplayMotion.ActionOwnerIdentity,
                gameplayMotion.GameplayResultOwnerIdentity);
            return new BodyMotionPrepareResult(plan, motion);
        }

        public static WorldBodyState Finalize(
            WorldBodyState beforeBody,
            BodyMotionIntegrationPlan plan,
            Float32Vector3 finalPosition,
            Float32Yaw finalYaw,
            Float32Vector3 appliedDisplacement,
            bool grounded,
            WorldCollisionSummary collision,
            Float32Scalar tickDelta)
        {
            RequirePlan(beforeBody, plan, tickDelta);
            Float32Scalar verticalVelocity = plan.CandidateVerticalVelocity;
            if (verticalVelocity < Float32Scalar.Zero && grounded)
                verticalVelocity = Float32Scalar.Zero;
            else if (verticalVelocity > Float32Scalar.Zero && (collision & WorldCollisionSummary.Above) != 0)
                verticalVelocity = Float32Scalar.Zero;
            return new WorldBodyState(
                beforeBody.ActorId,
                finalPosition,
                finalYaw,
                new Float32Vector3(
                    appliedDisplacement.X / tickDelta,
                    appliedDisplacement.Y / tickDelta,
                    appliedDisplacement.Z / tickDelta),
                verticalVelocity,
                grounded,
                collision);
        }

        static void RequirePlan(WorldBodyState beforeBody, BodyMotionIntegrationPlan plan, Float32Scalar tickDelta)
        {
            if (beforeBody.ActorId != plan.ActorId || tickDelta <= Float32Scalar.Zero || tickDelta != plan.TickDelta ||
                beforeBody.VerticalVelocity != plan.PreviousVerticalVelocity)
            {
                throw new InvalidOperationException("Body Motion integration plan does not match the World solve input.");
            }
        }

    }
}
