namespace ThirdPersonSimulation
{
    public enum BodyMotionMode : byte
    {
        FreeFall,
        Grappling = 1,
        AuthoredTrajectory = 3
    }

    public enum GrappleEndReason : byte
    {
        None,
        Arrived,
        Blocked,
        Interrupted,
        Released,
        Missed,
        OutOfRange,
        RopeCapacity,
        Landed
    }

    public enum GrapplePhase : byte { Idle, Shooting, Attached, Retracting, Preparing }

    public readonly struct BodyMotionIntegrationPlan<TScalar, TVector3>
        where TScalar : struct
        where TVector3 : struct
    {
        internal BodyMotionIntegrationPlan(ActorId actorId, SimulationTick tick, TScalar tickDelta,
            TScalar previousVerticalVelocity, TScalar candidateVerticalVelocity, TVector3 requestedDisplacement)
        {
            ActorId = actorId;
            Tick = tick;
            TickDelta = tickDelta;
            PreviousVerticalVelocity = previousVerticalVelocity;
            CandidateVerticalVelocity = candidateVerticalVelocity;
            RequestedDisplacement = requestedDisplacement;
        }

        public ActorId ActorId { get; }
        public SimulationTick Tick { get; }
        public TScalar TickDelta { get; }
        public TScalar PreviousVerticalVelocity { get; }
        public TScalar CandidateVerticalVelocity { get; }
        public TVector3 RequestedDisplacement { get; }
    }

    public static class BodyMotionIntegration<TScalar, TVector2, TVector3, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
    {
        public static BodyMotionIntegrationPlan<TScalar, TVector3> Prepare(
            ActorId actorId, SimulationTick tick, BodyMotionMode mode,
            TScalar previousVerticalVelocity, TVector3 gameplayDisplacement,
            in BodyMotionParameters<TScalar> parameters, TScalar tickDelta)
        {
            TMath math = default;
            if (mode == BodyMotionMode.Grappling ||
                mode == BodyMotionMode.AuthoredTrajectory)
                return new BodyMotionIntegrationPlan<TScalar, TVector3>(actorId, tick, tickDelta,
                    previousVerticalVelocity, math.Divide(math.Y3(gameplayDisplacement), tickDelta), gameplayDisplacement);
            TScalar candidate = math.Max(
                math.Add(previousVerticalVelocity, math.Multiply(parameters.GravityAcceleration, tickDelta)),
                math.Negate(parameters.MaximumFallSpeed));
            TVector3 displacement = math.AddVertical(gameplayDisplacement, math.Multiply(candidate, tickDelta));
            return new BodyMotionIntegrationPlan<TScalar, TVector3>(
                actorId, tick, tickDelta, previousVerticalVelocity, candidate, displacement);
        }

        public static TScalar ResolveVerticalVelocity(TScalar candidate, bool grounded, WorldCollisionSummary collision)
        {
            TMath math = default;
            if (grounded && math.GreaterThan(math.Zero, candidate) ||
                (collision & WorldCollisionSummary.Above) != 0 && math.GreaterThan(candidate, math.Zero))
                return math.Zero;
            return candidate;
        }
    }
}
