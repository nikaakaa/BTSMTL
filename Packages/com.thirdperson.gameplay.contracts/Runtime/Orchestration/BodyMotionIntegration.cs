namespace ThirdPersonSimulation
{
    public readonly struct BodyMotionParameters<TScalar> where TScalar : struct
    {
        public BodyMotionParameters(TScalar gravityAcceleration, TScalar maximumFallSpeed)
        {
            GravityAcceleration = gravityAcceleration;
            MaximumFallSpeed = maximumFallSpeed;
        }

        public TScalar GravityAcceleration { get; }
        public TScalar MaximumFallSpeed { get; }
    }

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

    public readonly struct BodyMotionPrepareResult<TScalar, TVector3, TRequest>
        where TScalar : struct
        where TVector3 : struct
        where TRequest : struct
    {
        internal BodyMotionPrepareResult(BodyMotionIntegrationPlan<TScalar, TVector3> plan, TRequest motion)
        {
            Plan = plan;
            Motion = motion;
        }

        public BodyMotionIntegrationPlan<TScalar, TVector3> Plan { get; }
        public TRequest Motion { get; }
    }

    public static class BodyMotionIntegration<TScalar, TVector2, TVector3, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
    {
        public static BodyMotionIntegrationPlan<TScalar, TVector3> Prepare(
            ActorId actorId, SimulationTick tick, TScalar previousVerticalVelocity, TVector3 gameplayDisplacement,
            in BodyMotionParameters<TScalar> parameters, TScalar tickDelta)
        {
            TMath math = default;
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
