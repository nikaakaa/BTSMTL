using BodyMotionIntegrationPlan = ThirdPersonSimulation.BodyMotionIntegrationPlan<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector3>;
using BodyMotionPrepareResult = ThirdPersonSimulation.BodyMotionPrepareResult<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector3, ThirdPersonSimulation.CharacterMotionRequest>;
using ResolvedGameplayMotion = ThirdPersonSimulation.ResolvedGameplayMotion<ThirdPersonSimulation.Float32Scalar, ThirdPersonSimulation.Float32Vector2, ThirdPersonSimulation.Float32Vector3>;
using System;

namespace ThirdPersonSimulation
{
    public sealed class CharacterMotionPreview
    {
        readonly SimulationMotionContribution<Float32Scalar, Float32Vector2, Float32Vector3, Float32SimulationMath>[] m_Contributions;
        int m_Count;

        public CharacterMotionPreview(int capacity) =>
            m_Contributions = new SimulationMotionContribution<Float32Scalar, Float32Vector2, Float32Vector3, Float32SimulationMath>[capacity];

        public void Clear()
        {
            Array.Clear(m_Contributions, 0, m_Count);
            m_Count = 0;
        }

        public void Add(CanonicalTextValue sourceIdentity, Float32Vector3 displacement, Float32Scalar yawDegrees,
            Float32Scalar weight, int priority, AbilityTimelineMotionSpace space, AbilityTimelineMotionChannel channel,
            AbilityTimelineMotionBlendMode blendMode, bool consumeLowerChannels)
        {
            m_Contributions[m_Count++] = new(sourceIdentity, displacement, yawDegrees, weight, priority,
                (SimulationMotionContributionSpace)space, (SimulationMotionChannel)channel,
                (SimulationMotionBlendMode)blendMode, consumeLowerChannels);
        }

        public ResolvedGameplayMotion Resolve(Float32Yaw yaw)
        {
            var action = CharacterMotionResolver<Float32Scalar, Float32Vector2, Float32Vector3, Float32Yaw, Float32SimulationMath>
                .ResolveChannel(m_Contributions, m_Count, yaw, SimulationMotionChannel.Action);
            var result = CharacterMotionResolver<Float32Scalar, Float32Vector2, Float32Vector3, Float32Yaw, Float32SimulationMath>
                .ResolveChannel(m_Contributions, m_Count, yaw, SimulationMotionChannel.GameplayResult);
            return CharacterMotionResolver<Float32Scalar, Float32Vector2, Float32Vector3, Float32Yaw, Float32SimulationMath>
                .Compose(default, in action, in result);
        }
    }

    public static class CharacterBodyMotionRuntime
    {
        public static BodyMotionPrepareResult Prepare(
            ActorId actorId,
            SimulationTick tick,
            in WorldBodyState beforeBody,
            in ResolvedGameplayMotion gameplayMotion,
            in BodyMotionParameters<Float32Scalar> parameters,
            Float32Scalar tickDelta)
        {
            if (!actorId.IsValid || !tick.IsValid || beforeBody.ActorId != actorId)
                throw new ArgumentException("Body Motion Prepare identity is inconsistent.");
            if (tickDelta <= Float32Scalar.Zero)
                throw new ArgumentOutOfRangeException(nameof(tickDelta));

            BodyMotionIntegrationPlan plan = BodyMotionIntegration<Float32Scalar, Float32Vector2, Float32Vector3, Float32SimulationMath>.Prepare(
                actorId, tick, beforeBody.VerticalVelocity, gameplayMotion.Displacement, in parameters, tickDelta);
            Float32Vector3 requestedDisplacement = plan.RequestedDisplacement;
            Float32Vector3 requestedVelocity = default(Float32SimulationMath).DivideVector3(requestedDisplacement, tickDelta);
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
            in WorldBodyState beforeBody,
            in BodyMotionIntegrationPlan plan,
            Float32Vector3 finalPosition,
            Float32Yaw finalYaw,
            Float32Vector3 appliedDisplacement,
            bool grounded,
            WorldCollisionSummary collision,
            Float32Scalar tickDelta)
        {
            RequirePlan(in beforeBody, in plan, tickDelta);
            Float32Scalar verticalVelocity = BodyMotionIntegration<Float32Scalar, Float32Vector2, Float32Vector3, Float32SimulationMath>.ResolveVerticalVelocity(
                plan.CandidateVerticalVelocity, grounded, collision);
            return new WorldBodyState(
                beforeBody.ActorId,
                finalPosition,
                finalYaw,
                default(Float32SimulationMath).DivideVector3(appliedDisplacement, tickDelta),
                verticalVelocity,
                grounded,
                collision);
        }

        static void RequirePlan(in WorldBodyState beforeBody, in BodyMotionIntegrationPlan plan, Float32Scalar tickDelta)
        {
            if (beforeBody.ActorId != plan.ActorId || tickDelta <= Float32Scalar.Zero || tickDelta != plan.TickDelta ||
                beforeBody.VerticalVelocity != plan.PreviousVerticalVelocity)
            {
                throw new InvalidOperationException("Body Motion integration plan does not match the World solve input.");
            }
        }

    }
}
