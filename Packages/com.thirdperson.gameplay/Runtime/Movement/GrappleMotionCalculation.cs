namespace ThirdPersonSimulation
{
    public static class GrappleMotionCalculation<TScalar, TVector2, TVector3, TMath>
        where TScalar : struct
        where TVector2 : struct
        where TVector3 : struct
        where TMath : struct, IScalarMath<TScalar>, IVectorMath<TScalar, TVector2, TVector3>
    {
        public static TScalar RopeTransitionBlend(TScalar age, TScalar duration)
        {
            TMath math = default;
            TScalar progress = math.Divide(age, duration);
            return math.Multiply(math.Multiply(progress, progress),
                math.Subtract(math.FromInt32(3), math.Multiply(math.FromInt32(2), progress)));
        }

        public static TVector3 BrakeRopeVelocity(TVector3 velocity, TVector3 pull,
            TScalar initialRadialSpeed, TScalar blend)
        {
            TMath math = default;
            TScalar target = math.Multiply(initialRadialSpeed, math.Subtract(math.One, blend));
            return math.AddVector3(velocity, math.MultiplyVector3(pull,
                math.Subtract(target, math.Dot3(velocity, pull))));
        }

        public static TVector3 ConstrainRopeDisplacement(TVector3 origin, TVector3 velocity,
            TVector3 anchor, TScalar length, TScalar delta, out TVector3 constrainedVelocity)
        {
            TMath math = default;
            TVector3 radial = math.Normalize3(math.SubtractVector3(origin, anchor));
            TVector3 tangent = math.SubtractVector3(velocity,
                math.MultiplyVector3(radial, math.Dot3(velocity, radial)));
            TVector3 predicted = math.AddVector3(origin, math.MultiplyVector3(tangent, delta));
            TVector3 nextRadial = math.Normalize3(math.SubtractVector3(predicted, anchor));
            TVector3 target = math.AddVector3(anchor, math.MultiplyVector3(nextRadial, length));
            constrainedVelocity = math.SubtractVector3(tangent,
                math.MultiplyVector3(nextRadial, math.Dot3(tangent, nextRadial)));
            TScalar speed = math.Magnitude3(tangent);
            if (math.GreaterThan(speed, math.Zero))
                constrainedVelocity = math.MultiplyVector3(math.Normalize3(constrainedVelocity), speed);
            return math.SubtractVector3(target, origin);
        }

        public static TScalar TurnYaw(TScalar targetDelta, TScalar lateralInput,
            TScalar turnSpeed, TScalar inputTurnSpeedBonus, TScalar delta)
        {
            TMath math = default;
            TScalar sameSideInput = math.Max(math.Zero,
                math.GreaterThan(targetDelta, math.Zero) ? lateralInput : math.Negate(lateralInput));
            TScalar maximumStep = math.Multiply(math.Add(turnSpeed,
                math.Multiply(inputTurnSpeedBonus, sameSideInput)), delta);
            return math.Clamp(targetDelta, math.Negate(maximumStep), maximumStep);
        }

        public static TVector3 ArrivalPosition(TVector3 start, TVector3 startVelocity,
            TVector3 target, TVector3 exitVelocity, TScalar duration, TScalar progress)
        {
            TMath math = default;
            TScalar squared = math.Multiply(progress, progress);
            TScalar cubed = math.Multiply(squared, progress);
            TScalar twiceCubed = math.Multiply(math.FromInt32(2), cubed);
            TScalar threeSquared = math.Multiply(math.FromInt32(3), squared);
            TScalar startWeight = math.Add(math.Subtract(twiceCubed, threeSquared), math.One);
            TScalar startTangent = math.Add(math.Subtract(cubed,
                math.Multiply(math.FromInt32(2), squared)), progress);
            TScalar targetWeight = math.Subtract(threeSquared, twiceCubed);
            TScalar targetTangent = math.Subtract(cubed, squared);
            return math.AddVector3(
                math.AddVector3(math.MultiplyVector3(start, startWeight),
                    math.MultiplyVector3(startVelocity, math.Multiply(startTangent, duration))),
                math.AddVector3(math.MultiplyVector3(target, targetWeight),
                    math.MultiplyVector3(exitVelocity, math.Multiply(targetTangent, duration))));
        }

        public static TScalar GravityScale(TVector3 pull, TVector3 look, TVector2 move,
            TScalar minimum, TScalar maximum)
        {
            TMath math = default;
            if (math.GreaterThan(math.Divide(math.FromInt32(-4), math.FromInt32(5)), math.Y3(pull)))
                return math.One;
            TVector2 horizontalLook = math.Normalize2(math.Vector2(math.X3(look), math.Z3(look)));
            TScalar forwardInput = math.Dot2(move, horizontalLook);
            TVector2 horizontalPull = math.Normalize2(math.Vector2(math.X3(pull), math.Z3(pull)));
            TScalar awayInput = math.Negate(math.Dot2(move, horizontalPull));
            TVector3 up = math.Vector3(math.Zero, math.One, math.Zero);
            TVector3 ropeUp = math.Normalize3(math.SubtractVector3(up, math.MultiplyVector3(pull, math.Y3(pull))));
            TScalar underInput = math.Multiply(
                math.Multiply(math.Multiply(math.Negate(forwardInput), math.Dot3(ropeUp, look)), math.FromInt32(2)),
                math.Clamp(math.Multiply(math.Multiply(math.Negate(forwardInput), math.Y3(look)), math.FromInt32(10)),
                    math.Zero, math.One));
            TScalar blend = math.Clamp(math.Max(awayInput, underInput), math.Zero, math.One);
            return math.Add(minimum, math.Multiply(math.Subtract(maximum, minimum), blend));
        }

        public static TVector3 PullVelocity(TVector3 velocity, TVector3 pull, TVector3 look,
            TScalar startSpeed, TScalar maximumSpeed, TScalar rampProgress,
            TScalar acceleration, TScalar tangentialDeceleration, TScalar delta)
        {
            TMath math = default;
            TScalar vertical = math.Y3(velocity);
            TScalar along = math.Dot3(velocity, pull);
            TVector3 tangent = math.SubtractVector3(velocity, math.MultiplyVector3(pull, along));
            TVector3 preserved = math.Vector3(math.Zero, math.Min(math.Y3(tangent), math.Zero), math.Zero);
            tangent = math.SubtractVector3(tangent, preserved);
            TVector3 viewVelocity = math.MultiplyVector3(look, math.Max(math.Zero, math.Dot3(tangent, look)));
            preserved = math.AddVector3(preserved, viewVelocity);
            tangent = math.SubtractVector3(tangent, viewVelocity);
            TScalar tangentSpeed = math.Magnitude3(tangent);
            if (math.GreaterThan(tangentSpeed, math.Zero))
                tangent = math.MultiplyVector3(tangent, math.Divide(
                    math.Max(math.Zero, math.Subtract(tangentSpeed, math.Multiply(tangentialDeceleration, delta))), tangentSpeed));
            TScalar limit = math.Add(startSpeed, math.Multiply(math.Subtract(maximumSpeed, startSpeed), rampProgress));
            if (math.GreaterThan(limit, along))
                along = math.Min(limit, math.Add(along, math.Multiply(acceleration, delta)));
            velocity = math.AddVector3(math.AddVector3(math.MultiplyVector3(pull, along), tangent), preserved);
            return math.Vector3(math.X3(velocity), math.Max(vertical, math.Y3(velocity)), math.Z3(velocity));
        }
    }
}
