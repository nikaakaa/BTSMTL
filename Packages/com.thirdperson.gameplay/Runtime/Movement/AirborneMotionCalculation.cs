namespace ThirdPersonSimulation
{
    public readonly struct AirborneMotionParameters<TScalar> where TScalar : struct
    {
        public AirborneMotionParameters(TScalar maximumSpeed, TScalar acceleration, TScalar turnSpeed,
            TScalar inputTurnSpeedBonus, TScalar trajectoryTurnSpeed, TScalar trajectoryInputTurnSpeedBonus,
            TScalar maximumHeadingLag, TScalar alignmentSpeed)
        {
            MaximumSpeed = maximumSpeed;
            Acceleration = acceleration;
            TurnSpeed = turnSpeed;
            InputTurnSpeedBonus = inputTurnSpeedBonus;
            TrajectoryTurnSpeed = trajectoryTurnSpeed;
            TrajectoryInputTurnSpeedBonus = trajectoryInputTurnSpeedBonus;
            MaximumHeadingLag = maximumHeadingLag;
            AlignmentSpeed = alignmentSpeed;
        }

        public readonly TScalar MaximumSpeed;
        public readonly TScalar Acceleration;
        public readonly TScalar TurnSpeed;
        public readonly TScalar InputTurnSpeedBonus;
        public readonly TScalar TrajectoryTurnSpeed;
        public readonly TScalar TrajectoryInputTurnSpeedBonus;
        public readonly TScalar MaximumHeadingLag;
        public readonly TScalar AlignmentSpeed;
    }

    public static class AirborneMotionCalculation<TScalar, TVector2, TVector3, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>,
            IYawMath<TScalar, TScalar>, IPlanarRotation<TVector3, TScalar>
    {
        public static TVector3 Advance(TVector3 velocity, TVector2 move, TScalar bodyYaw,
            in AirborneMotionParameters<TScalar> parameters, TScalar delta, out TScalar yawDelta)
        {
            TMath math = default;
            TScalar vertical = math.Y3(velocity);
            TVector3 planar = math.Vector3(math.X3(velocity), math.Zero, math.Z3(velocity));
            TVector3 candidate = AirMotionCalculation<TScalar, TVector2, TVector3, TMath>.Steer(
                planar, move, parameters.MaximumSpeed, parameters.Acceleration, parameters.Acceleration, delta);
            if (math.IsVector3Zero(candidate))
            {
                yawDelta = math.Zero;
                return math.Vector3(math.Zero, vertical, math.Zero);
            }

            TScalar speed = math.Magnitude3(candidate);
            TScalar candidateYaw = math.Degrees(math.YawFromDirection(math.X3(candidate), math.Z3(candidate)));
            TVector3 right = math.RotatePlanar(math.Vector3(math.One, math.Zero, math.Zero), bodyYaw);
            TScalar lateral = math.Clamp(math.Add(math.Multiply(math.X2(move), math.X3(right)),
                math.Multiply(math.Y2(move), math.Z3(right))), math.Negate(math.One), math.One);
            TScalar heading = candidateYaw;
            TScalar turn = math.DeltaYaw(bodyYaw, candidateYaw);
            if (!math.IsVector2Zero(move) && math.GreaterThan(speed, parameters.AlignmentSpeed))
            {
                TScalar beforeYaw = math.IsVector3Zero(planar) ? bodyYaw :
                    math.Degrees(math.YawFromDirection(math.X3(planar), math.Z3(planar)));
                TScalar trajectoryDelta = TurnStep(math.DeltaYaw(beforeYaw, candidateYaw), lateral,
                    parameters.TrajectoryTurnSpeed, parameters.TrajectoryInputTurnSpeedBonus, delta);
                heading = math.Add(beforeYaw, trajectoryDelta);
                TScalar bodyStep = TurnStep(math.DeltaYaw(bodyYaw, heading), lateral,
                    parameters.TurnSpeed, parameters.InputTurnSpeedBonus, delta);
                TScalar previousLag = math.DeltaYaw(math.Add(bodyYaw, bodyStep), beforeYaw);
                trajectoryDelta = math.Clamp(trajectoryDelta,
                    math.Min(math.Zero, math.Subtract(math.Negate(parameters.MaximumHeadingLag), previousLag)),
                    math.Max(math.Zero, math.Subtract(parameters.MaximumHeadingLag, previousLag)));
                heading = math.Add(beforeYaw, trajectoryDelta);
                TVector3 direction = math.RotatePlanar(math.Vector3(math.Zero, math.Zero, math.One), heading);
                speed = math.Max(math.Zero, math.Dot3(candidate, direction));
                candidate = math.MultiplyVector3(direction, speed);
                turn = math.DeltaYaw(bodyYaw, heading);
            }

            yawDelta = TurnStep(turn, lateral, parameters.TurnSpeed, parameters.InputTurnSpeedBonus, delta);
            if (!math.IsVector2Zero(move) && math.GreaterThan(
                math.Abs(math.DeltaYaw(math.Add(bodyYaw, yawDelta), heading)), parameters.MaximumHeadingLag))
            {
                TScalar allowedSpeed = math.Max(math.Magnitude3(planar), parameters.AlignmentSpeed);
                if (math.GreaterThan(speed, allowedSpeed))
                    candidate = math.MultiplyVector3(candidate, math.Divide(allowedSpeed, speed));
            }
            return math.Vector3(math.X3(candidate), vertical, math.Z3(candidate));
        }

        static TScalar TurnStep(TScalar target, TScalar lateral, TScalar speed, TScalar bonus, TScalar delta)
        {
            TMath math = default;
            TScalar sameSide = math.Max(math.Zero, math.GreaterThan(target, math.Zero) ? lateral : math.Negate(lateral));
            TScalar step = math.Multiply(math.Add(speed, math.Multiply(bonus, sameSide)), delta);
            return math.Clamp(target, math.Negate(step), step);
        }
    }
}
